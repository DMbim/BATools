// File: BA_Tools/CadPurge/ViewModels/CadPurgeViewModel.cs
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
using CommunityToolkit.Mvvm.ComponentModel;

namespace BA.CadPurge.ViewModels
{
    /// <summary>
    /// Top-level ViewModel for the CAD Purge tool window. Deliberately holds no cached Document or
    /// UIDocument reference: this is a modeless window (see CadPurgeCommand, Stage 5), so the
    /// active document can change while the window stays open. Every operation re-resolves
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

        public BA.UI.Mvvm.RelayCommand ScanCommand { get; }
        public BA.UI.Mvvm.RelayCommand ApplySelectedCommand { get; }
        public BA.UI.Mvvm.RelayCommand SelectAllMappableCommand { get; }
        public BA.UI.Mvvm.RelayCommand ClearSelectionCommand { get; }

        public CadPurgeViewModel()
        {
            _mappingConfigService = new MappingConfigService();
            _scanService = new PurgeScanService(_mappingConfigService);
            _templateLoader = new CorporateTemplateLoader();
            _resolverService = new CorporateStandardResolverService(_templateLoader);
            _batchExecutor = new PurgeBatchExecutor(_resolverService, new LinePatternMappingService(), new TextStyleMappingService());

            ScanCommand = new BA.UI.Mvvm.RelayCommand(_ => Scan(), _ => !IsBusy);
            ApplySelectedCommand = new BA.UI.Mvvm.RelayCommand(_ => ApplySelected(), _ => !IsBusy && HasActionableSelection());
            SelectAllMappableCommand = new BA.UI.Mvvm.RelayCommand(_ => SelectAllMappable(), _ => !IsBusy);
            ClearSelectionCommand = new BA.UI.Mvvm.RelayCommand(_ => ClearSelection(), _ => !IsBusy);

            LinePatternCandidates.CollectionChanged += (_, __) => OnPropertyChanged(nameof(TotalCandidateCount));
            TextStyleCandidates.CollectionChanged += (_, __) => OnPropertyChanged(nameof(TotalCandidateCount));
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

            AppExternalInvoker.Instance.Run(
                uiApp =>
                {
                    Document doc = uiApp.ActiveUIDocument?.Document
                        ?? throw new InvalidOperationException("No active document. Open a project document before scanning.");

                    TemplateBaselineSnapshot baseline = _templateLoader.LoadBaseline(uiApp.Application, config.TemplateFilePath);
                    List<PurgeCandidate> lineAndTextCandidates = _scanService.ScanLinePatternsAndTextStyles(doc, config, baseline);
                    List<DwgImportReportEntry> dwgReport = _scanService.ScanDwgImports(doc);

                    return (lineAndTextCandidates, dwgReport);
                },
                result =>
                {
                    var (candidates, dwgReport) = result;

                    foreach (PurgeCandidateViewModel existing in LinePatternCandidates.Concat(TextStyleCandidates))
                        existing.PropertyChanged -= OnCandidateVmPropertyChanged;

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

        private void OnCandidateVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PurgeCandidateViewModel.SelectedAction))
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