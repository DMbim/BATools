// File: BA_Tools/CAD Purge/ViewModels/CadPurgeViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.CadPurge.Models;
using BA.CadPurge.Services;
using BA.UI.ExternalEvents;
using BA.UI.Mvvm;
using RelayCommand = BA.UI.Mvvm.RelayCommand;


namespace BA.CadPurge.ViewModels
{
    /// <summary>
    /// Top-level ViewModel for the CAD Purge tool window. Deliberately holds no cached Document or
    /// UIDocument reference: this is a modeless window (see CadPurgeCommand), so the active
    /// document can change while the window stays open. Every operation re-resolves
    /// uiApp.ActiveUIDocument fresh, inside the AppExternalInvoker callback, at the moment the
    /// operation actually runs.
    ///
    /// All Revit-API-touching work (Scan, ApplySelected) is routed through
    /// AppExternalInvoker.Instance.Run(...), which marshals the queued work onto the Revit UI
    /// thread and marshals the completion/error callbacks back onto this ViewModel's WPF
    /// dispatcher. Nothing in this class calls the Revit API directly on the calling thread.
    /// </summary>
    public sealed class CadPurgeViewModel : BA.UI.Mvvm.ObservableObject
    {
        private readonly MappingConfigService _mappingConfigService;
        private readonly PurgeScanService _scanService;
        private readonly CorporateTemplateLoader _templateLoader;
        private readonly CorporateStandardResolverService _resolverService;
        private readonly PurgeBatchExecutor _batchExecutor;

        private MappingConfig _loadedConfig;

        /// <summary>
        /// The set of candidate row view models currently highlighted together in the active
        /// DataGrid, as reported by CadPurgeWindow's SelectionChanged bridge (see
        /// SetHighlightedGroup). When SelectedAction changes on a member of a group with more than
        /// one entry, that same action is applied to every other member. Deliberately named
        /// "Highlighted" rather than "Selected" to avoid colliding with the existing meaning of
        /// SelectedAction and ApplySelectedCommand, which refer to a candidate having an action
        /// queued, not to a row being highlighted in the grid.
        /// </summary>
        private readonly List<PurgeCandidateViewModel> _highlightedGroup = new();

        /// <summary>
        /// Guards against re-entrant propagation while OnCandidateVmPropertyChanged is itself
        /// setting SelectedAction on the other members of the highlighted group, since each of
        /// those assignments raises its own PropertyChanged back into this same handler.
        /// </summary>
        private bool _isApplyingGroupAction;

        public ObservableCollection<PurgeCandidateViewModel> LinePatternCandidates { get; } = new();
        public ObservableCollection<PurgeCandidateViewModel> TextStyleCandidates { get; } = new();
        public ObservableCollection<DwgImportReportEntry> DwgImportReport { get; } = new();

        public int TotalCandidateCount => LinePatternCandidates.Count + TextStyleCandidates.Count;

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        private string _statusMessage = "Click Scan to inventory this document's line patterns, text styles, and DWG imports.";
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public RelayCommand ScanCommand { get; }
        public RelayCommand ApplySelectedCommand { get; }
        public RelayCommand SelectAllMappableCommand { get; }
        public RelayCommand ClearSelectionCommand { get; }

        public CadPurgeViewModel()
        {
            _mappingConfigService = new MappingConfigService();
            _scanService = new PurgeScanService(_mappingConfigService);
            _templateLoader = new CorporateTemplateLoader();
            _resolverService = new CorporateStandardResolverService(_templateLoader);
            _batchExecutor = new PurgeBatchExecutor(_resolverService, new LinePatternMappingService(), new TextStyleMappingService());

            ScanCommand = new RelayCommand(_ => Scan(), _ => !IsBusy);
            ApplySelectedCommand = new RelayCommand(_ => ApplySelected(), _ => !IsBusy && HasActionableSelection());
            SelectAllMappableCommand = new RelayCommand(_ => SelectAllMappable(), _ => !IsBusy);
            ClearSelectionCommand = new RelayCommand(_ => ClearSelection(), _ => !IsBusy);

            LinePatternCandidates.CollectionChanged += (_, __) => OnPropertyChanged(nameof(TotalCandidateCount));
            TextStyleCandidates.CollectionChanged += (_, __) => OnPropertyChanged(nameof(TotalCandidateCount));
        }

        /// <summary>
        /// Called by CadPurgeWindow whenever a DataGrid's row highlight settles, after applying
        /// its own "sticky" logic to survive WPF collapsing a multi row highlight down to one row
        /// when an interactive cell control is clicked. Replaces the current highlighted group
        /// wholesale; the caller is expected to pass the full, current membership each time, not a
        /// delta.
        /// </summary>
        public void SetHighlightedGroup(IEnumerable<PurgeCandidateViewModel> items)
        {
            _highlightedGroup.Clear();
            _highlightedGroup.AddRange(items);
        }

        private bool HasActionableSelection()
        {
            return LinePatternCandidates.Concat(TextStyleCandidates).Any(vm => vm.SelectedAction != PurgeAction.None);
        }

        private void Scan()
        {
            if (!_mappingConfigService.TryLoad(out MappingConfig config, out string configError))
            {
                StatusMessage = configError;
                return;
            }

            _loadedConfig = config;
            IsBusy = true;
            StatusMessage = "Scanning active document...";
            RaiseCommandsCanExecuteChanged();

            AppLogger.LogInfo("[CadPurge] Scan: starting. Raising ExternalEvent."); // <- NEW

            AppExternalInvoker.Instance.Run(
                uiApp =>
                {
                    AppLogger.LogInfo("[CadPurge] Scan: ExternalEvent fired, running on Revit thread. Resolving active document..."); // <- NEW

                    Document doc = uiApp.ActiveUIDocument?.Document
                        ?? throw new InvalidOperationException("No active document. Open a project document before scanning.");

                    AppLogger.LogInfo($"[CadPurge] Scan: active document = '{doc.Title}'. Opening reference template '{config.TemplateFilePath}' for baseline..."); // <- NEW

                    TemplateBaselineSnapshot baseline = _templateLoader.LoadBaseline(uiApp.Application, config.TemplateFilePath);

                    AppLogger.LogInfo($"[CadPurge] Scan: baseline loaded ({baseline.LinePatternNames.Count} line pattern name(s), {baseline.TextStyleNames.Count} text style name(s)). Scanning line patterns and text styles..."); // <- NEW

                    List<PurgeCandidate> lineAndTextCandidates = _scanService.ScanLinePatternsAndTextStyles(doc, config, baseline);

                    AppLogger.LogInfo($"[CadPurge] Scan: found {lineAndTextCandidates.Count} line pattern/text style candidate(s). Scanning DWG imports..."); // <- NEW

                    List<DwgImportReportEntry> dwgReport = _scanService.ScanDwgImports(doc);

                    AppLogger.LogInfo($"[CadPurge] Scan: found {dwgReport.Count} DWG import(s). Scan complete, returning to UI thread."); // <- NEW

                    return (lineAndTextCandidates, dwgReport);
                },
                result =>
                {
                    var (candidates, dwgReport) = result;

                    foreach (PurgeCandidateViewModel existing in LinePatternCandidates.Concat(TextStyleCandidates))
                        existing.PropertyChanged -= OnCandidateVmPropertyChanged;

                    _highlightedGroup.Clear();

                    LinePatternCandidates.Clear();
                    TextStyleCandidates.Clear();
                    DwgImportReport.Clear();

                    foreach (PurgeCandidate candidate in candidates)
                    {
                        var vm = new PurgeCandidateViewModel(candidate);
                        vm.PropertyChanged += OnCandidateVmPropertyChanged;

                        if (candidate.ItemType == PurgeItemType.LinePattern)
                            LinePatternCandidates.Add(vm);
                        else
                            TextStyleCandidates.Add(vm);
                    }

                    foreach (DwgImportReportEntry entry in dwgReport)
                        DwgImportReport.Add(entry);

                    StatusMessage = $"Scan complete. {LinePatternCandidates.Count} line pattern(s), {TextStyleCandidates.Count} text style(s), {DwgImportReport.Count} DWG import(s) found.";
                    IsBusy = false;
                    RaiseCommandsCanExecuteChanged();
                },
                ex =>
                {
                    StatusMessage = $"Scan failed: {ex.Message}";
                    AppLogger.LogError("CadPurgeViewModel.Scan", ex);
                    IsBusy = false;
                    RaiseCommandsCanExecuteChanged();
                });
        }

        private void ApplySelected()
        {
            if (_loadedConfig == null)
            {
                StatusMessage = "Run Scan before applying changes.";
                return;
            }

            List<PurgeCandidate> actionable = LinePatternCandidates.Concat(TextStyleCandidates)
                .Where(vm => vm.SelectedAction != PurgeAction.None)
                .Select(vm => vm.Model)
                .ToList();

            if (actionable.Count == 0)
            {
                StatusMessage = "No candidates selected for Delete or Map.";
                return;
            }

            IsBusy = true;
            StatusMessage = $"Applying {actionable.Count} change(s)...";
            RaiseCommandsCanExecuteChanged();

            MappingConfig config = _loadedConfig;

            AppExternalInvoker.Instance.Run(
                uiApp =>
                {
                    Document doc = uiApp.ActiveUIDocument?.Document
                        ?? throw new InvalidOperationException("No active document. It may have been closed since Scan ran.");

                    return _batchExecutor.ExecuteBatch(doc, config, actionable);
                },
                result =>
                {
                    foreach (PurgeCandidateViewModel vm in LinePatternCandidates.Concat(TextStyleCandidates))
                        vm.RefreshFromModel();

                    string warningNote = result.Warnings.Count > 0
                        ? $" {result.Warnings.Count} warning(s) were auto-resolved, see individual item status for detail."
                        : string.Empty;

                    StatusMessage = $"Applied. Succeeded: {result.Succeeded}, Failed: {result.Failed}, Skipped: {result.Skipped}.{warningNote}";
                    IsBusy = false;
                    RaiseCommandsCanExecuteChanged();
                },
                ex =>
                {
                    StatusMessage = $"Apply failed: {ex.Message}";
                    AppLogger.LogError("CadPurgeViewModel.ApplySelected", ex);
                    IsBusy = false;
                    RaiseCommandsCanExecuteChanged();
                });
        }

        private void SelectAllMappable()
        {
            foreach (PurgeCandidateViewModel vm in LinePatternCandidates.Concat(TextStyleCandidates))
            {
                if (vm.HasProposedMapping)
                    vm.SelectedAction = PurgeAction.MapToStandard;
            }

            RaiseCommandsCanExecuteChanged();
        }

        private void ClearSelection()
        {
            foreach (PurgeCandidateViewModel vm in LinePatternCandidates.Concat(TextStyleCandidates))
                vm.SelectedAction = PurgeAction.None;

            RaiseCommandsCanExecuteChanged();
        }

        /// <summary>
        /// Also carries the group propagation: when SelectedAction changes on a candidate that
        /// belongs to a highlighted group of more than one row, the same new action value is
        /// applied to every other member of that group. Siblings without a proposed mapping are
        /// left untouched when the new value is MapToStandard, since PurgeCandidateViewModel's own
        /// SelectedAction setter already no-ops that case; this mirrors what the per row dropdown
        /// has always done, it just now also happens as a side effect of editing a sibling.
        /// </summary>
        private void OnCandidateVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PurgeCandidateViewModel.SelectedAction))
                return;

            if (!_isApplyingGroupAction
                && sender is PurgeCandidateViewModel changedVm
                && _highlightedGroup.Count > 1
                && _highlightedGroup.Contains(changedVm))
            {
                _isApplyingGroupAction = true;
                try
                {
                    PurgeAction newAction = changedVm.SelectedAction;

                    foreach (PurgeCandidateViewModel vm in _highlightedGroup)
                    {
                        if (!ReferenceEquals(vm, changedVm))
                            vm.SelectedAction = newAction;
                    }
                }
                finally
                {
                    _isApplyingGroupAction = false;
                }
            }

            RaiseCommandsCanExecuteChanged();
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            ScanCommand.RaiseCanExecuteChanged();
            ApplySelectedCommand.RaiseCanExecuteChanged();
            SelectAllMappableCommand.RaiseCanExecuteChanged();
            ClearSelectionCommand.RaiseCanExecuteChanged();
        }
    }
}