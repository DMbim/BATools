// FILE: BA_Tools/Warnings/ViewModels/WarningsDashboardViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Warnings.ExternalEvents;
using BA.Warnings.Models;
using BA.Warnings.Services;
using BA.Warnings.Settings;

namespace BA.Warnings.ViewModels
{
    public sealed class WarningGroupViewModel : BA.UI.Mvvm.ObservableObject
    {
        public int ClassifiedSeverity { get; }
        public ObservableCollection<WarningItem> Items { get; } = new ObservableCollection<WarningItem>();

        public string Header => ClassifiedSeverity == 0
            ? $"Unclassified ({Items.Count})"
            : $"Severity {ClassifiedSeverity} ({Items.Count})";

        public WarningGroupViewModel(int classifiedSeverity)
        {
            ClassifiedSeverity = classifiedSeverity;
        }

        public void Refresh(IEnumerable<WarningItem> items)
        {
            Items.Clear();
            foreach (WarningItem i in items) Items.Add(i);
            OnPropertyChanged(nameof(Header));
        }
    }

    public sealed class JoinRuleRowViewModel : BA.UI.Mvvm.ObservableObject
    {
        public Guid FailureDefinitionGuid { get; }
        public string DescriptionSample { get; }
        public int OccurrenceCount { get; }

        private JoinResolutionAction _action;
        public JoinResolutionAction Action
        {
            get => _action;
            set { _action = value; OnPropertyChanged(nameof(Action)); }
        }

        public JoinRuleRowViewModel(Guid guid, string descriptionSample, int occurrenceCount, JoinResolutionAction action)
        {
            FailureDefinitionGuid = guid;
            DescriptionSample = descriptionSample;
            OccurrenceCount = occurrenceCount;
            _action = action;
        }
    }

    public sealed class WarningsDashboardViewModel : BA.UI.Mvvm.ObservableObject, IDisposable
    {
        private readonly UIApplication _uiApp;
        private readonly WarningsDashboardSettings _settings;
        private readonly DispatcherTimer _debounceTimer;

        private List<WarningItem> _allWarnings = new List<WarningItem>();

        public ObservableCollection<WarningGroupViewModel> Groups { get; } = new ObservableCollection<WarningGroupViewModel>();
        public ObservableCollection<JoinRuleRowViewModel> Rules { get; } = new ObservableCollection<JoinRuleRowViewModel>();
        public ObservableCollection<JoinResolutionPreviewItem> PreviewItems { get; } = new ObservableCollection<JoinResolutionPreviewItem>();
        public ObservableCollection<string> AvailableCategories { get; } = new ObservableCollection<string> { "All" };
        public ObservableCollection<DuplicateInstanceGroup> DuplicateGroups { get; } = new ObservableCollection<DuplicateInstanceGroup>();

        private string _selectedCategoryFilter = "All";
        public string SelectedCategoryFilter
        {
            get => _selectedCategoryFilter;
            set
            {
                _selectedCategoryFilter = value;
                OnPropertyChanged(nameof(SelectedCategoryFilter));
                ApplyFilterAndRegroup();
            }
        }

        private WarningItem _selectedWarning;
        public WarningItem SelectedWarning
        {
            get => _selectedWarning;
            set
            {
                _selectedWarning = value;
                OnPropertyChanged(nameof(SelectedWarning));
                ZoomToSelectedCommand.RaiseCanExecuteChanged();
                HighlightSelectedCommand.RaiseCanExecuteChanged();
            }
        }

        private bool _liveRefreshEnabled = true;
        public bool LiveRefreshEnabled
        {
            get => _liveRefreshEnabled;
            set { _liveRefreshEnabled = value; OnPropertyChanged(nameof(LiveRefreshEnabled)); }
        }

        private bool _previewPanelOpen;
        public bool PreviewPanelOpen
        {
            get => _previewPanelOpen;
            set { _previewPanelOpen = value; OnPropertyChanged(nameof(PreviewPanelOpen)); }
        }

        private bool _duplicateReviewPanelOpen;
        public bool DuplicateReviewPanelOpen
        {
            get => _duplicateReviewPanelOpen;
            set { _duplicateReviewPanelOpen = value; OnPropertyChanged(nameof(DuplicateReviewPanelOpen)); }
        }

        private string _statusText = "Ready.";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public BA.UI.Mvvm.RelayCommand RefreshCommand { get; }
        public BA.UI.Mvvm.RelayCommand ZoomToSelectedCommand { get; }
        public BA.UI.Mvvm.RelayCommand HighlightSelectedCommand { get; }
        public BA.UI.Mvvm.RelayCommand ClearHighlightsCommand { get; }
        public BA.UI.Mvvm.RelayCommand PreviewAutoResolveCommand { get; }
        public BA.UI.Mvvm.RelayCommand CommitAutoResolveCommand { get; }
        public BA.UI.Mvvm.RelayCommand SaveRulesCommand { get; }
        public BA.UI.Mvvm.RelayCommand ClosePreviewCommand { get; }
        public BA.UI.Mvvm.RelayCommand ReviewDuplicatesCommand { get; }
        public BA.UI.Mvvm.RelayCommand CommitDuplicateDeleteCommand { get; }
        public BA.UI.Mvvm.RelayCommand CloseDuplicateReviewCommand { get; }

        public WarningsDashboardViewModel(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _settings = WarningsDashboardSettings.Load<WarningsDashboardSettings>() ?? new WarningsDashboardSettings();
            _settings.SeedDefaultJoinRulesIfNeeded();

            foreach (int sev in new[] { 5, 4, 3, 2, 1, 0 })
            {
                Groups.Add(new WarningGroupViewModel(sev));
            }

            RefreshCommand = new BA.UI.Mvvm.RelayCommand(_ => Refresh());
            ZoomToSelectedCommand = new BA.UI.Mvvm.RelayCommand(_ => SafeInvoke("ZoomToSelected", ZoomToSelected), _ => SelectedWarning != null);
            HighlightSelectedCommand = new BA.UI.Mvvm.RelayCommand(_ => SafeInvoke("HighlightSelected", HighlightSelected), _ => SelectedWarning != null);
            ClearHighlightsCommand = new BA.UI.Mvvm.RelayCommand(_ => SafeInvoke("ClearHighlights", ClearHighlights));
            PreviewAutoResolveCommand = new BA.UI.Mvvm.RelayCommand(_ => SafeInvoke("PreviewAutoResolve", PreviewAutoResolve));
            CommitAutoResolveCommand = new BA.UI.Mvvm.RelayCommand(_ => SafeInvoke("CommitAutoResolve", CommitAutoResolve), _ => PreviewItems.Any(i => i.Include));
            SaveRulesCommand = new BA.UI.Mvvm.RelayCommand(_ => SafeInvoke("SaveRules", SaveRules));
            ClosePreviewCommand = new BA.UI.Mvvm.RelayCommand(_ => PreviewPanelOpen = false);
            ReviewDuplicatesCommand = new BA.UI.Mvvm.RelayCommand(_ => SafeInvoke("ReviewDuplicates", ReviewDuplicates));
            CommitDuplicateDeleteCommand = new BA.UI.Mvvm.RelayCommand(_ => SafeInvoke("CommitDuplicateDelete", CommitDuplicateDelete),
                _ => DuplicateGroups.Any(g => g.Candidates.Any(c => c.MarkedForDeletion)));
            CloseDuplicateReviewCommand = new BA.UI.Mvvm.RelayCommand(_ => DuplicateReviewPanelOpen = false);

            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                Refresh();
            };

            _uiApp.Application.DocumentChanged += OnDocumentChanged;

            Refresh();
        }

        // DIAGNOSTIC WRAPPER: every button-triggered action goes through this now.
        // Nothing in this codebase's ViewModels previously caught exceptions at
        // this layer, only the ExternalEvent handlers' own Execute methods did.
        // If a click updates status text once and then goes silent with no
        // TaskDialog and nothing in the log, something is throwing between the
        // click and the point where a handler's Execute ever runs, and this is
        // the layer needed to actually see it instead of it disappearing
        // upstream of our own logging entirely.
        private void SafeInvoke(string actionName, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"WarningsDashboardViewModel.{actionName}", ex);
                StatusText = $"{actionName} failed: {ex.Message} (see log)";
            }
        }

        private void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            if (!LiveRefreshEnabled) return;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void Refresh()
        {
            StatusText = "Refreshing...";
            RefreshWarningsHandler.RequestRefresh(items =>
            {
                _allWarnings = items;
                RebuildCategoryList(items);
                ApplyFilterAndRegroup();
                RebuildRuleRows(items);

                int unmatchedCount = items.Count(i => i.ClassifiedSeverity == 0);

                if (FailureClassificationService.Instance.LastLoadFailed)
                {
                    StatusText = $"Classification file unavailable ({FailureClassificationService.Instance.LastLoadError}). " +
                                 $"All {items.Count} warning(s) showing as Unclassified.";
                }
                else if (unmatchedCount > 0)
                {
                    StatusText = $"{items.Count} warning(s) loaded, {unmatchedCount} unclassified (logged for review).";
                }
                else
                {
                    StatusText = $"{items.Count} warning(s) loaded.";
                }
            });
        }

        private void RebuildCategoryList(List<WarningItem> items)
        {
            List<string> categories = items.Select(i => i.Category).Distinct().OrderBy(c => c).ToList();
            string previousSelection = SelectedCategoryFilter;

            AvailableCategories.Clear();
            AvailableCategories.Add("All");
            foreach (string c in categories) AvailableCategories.Add(c);

            _selectedCategoryFilter = AvailableCategories.Contains(previousSelection) ? previousSelection : "All";
            OnPropertyChanged(nameof(SelectedCategoryFilter));
        }

        private void ApplyFilterAndRegroup()
        {
            List<WarningItem> filtered = (SelectedCategoryFilter == "All"
                ? _allWarnings
                : _allWarnings.Where(i => i.Category == SelectedCategoryFilter)).ToList();

            foreach (WarningGroupViewModel group in Groups)
            {
                group.Refresh(filtered.Where(i => i.ClassifiedSeverity == group.ClassifiedSeverity));
            }
        }

        private void RebuildRuleRows(List<WarningItem> items)
        {
            var distinct = items
                .GroupBy(i => i.FailureDefinitionId.Guid)
                .Select(g => new { Guid = g.Key, Sample = g.First().Description, Count = g.Count() })
                .OrderByDescending(d => d.Count);

            Dictionary<Guid, JoinResolutionAction> existingRuleMap = _settings.JoinResolutionRules
                .ToDictionary(r => r.FailureDefinitionGuid, r => r.Action);

            Rules.Clear();
            foreach (var d in distinct)
            {
                JoinResolutionAction action = existingRuleMap.TryGetValue(d.Guid, out JoinResolutionAction a)
                    ? a
                    : JoinResolutionAction.Ignore;

                Rules.Add(new JoinRuleRowViewModel(d.Guid, d.Sample, d.Count, action));
            }
        }

        private void SaveRules()
        {
            _settings.JoinResolutionRules = Rules
                .Where(r => r.Action != JoinResolutionAction.Ignore)
                .Select(r => new JoinFailureResolutionRule
                {
                    FailureDefinitionGuid = r.FailureDefinitionGuid,
                    DisplayName = r.DescriptionSample,
                    Action = r.Action
                })
                .ToList();

            _settings.Save();
            StatusText = "Join resolution rules saved.";
        }

        public void LoadElementDetailsIfNeeded(WarningItem item)
        {
            if (item == null || item.DetailsLoaded) return;

            item.ElementDetails.Clear();
            item.ElementDetails.Add(new WarningElementDetail { DisplayText = "Loading..." });

            try
            {
                WarningElementDetailsHandler.RequestDetails(item, details =>
                {
                    item.ElementDetails.Clear();
                    foreach (WarningElementDetail d in details) item.ElementDetails.Add(d);
                    item.DetailsLoaded = true;
                });
            }
            catch (Exception ex)
            {
                AppLogger.LogError("WarningsDashboardViewModel.LoadElementDetailsIfNeeded", ex);
                item.ElementDetails.Clear();
                item.ElementDetails.Add(new WarningElementDetail { DisplayText = $"Failed to load details: {ex.Message}" });
            }
        }

        private void ZoomToSelected()
        {
            if (SelectedWarning == null) return;

            List<ElementId> ids = SelectedWarning.AllElementIds.ToList();
            StatusText = "Zooming...";
            ZoomToWarningElementsHandler.RequestZoom(ids, success =>
            {
                StatusText = success ? "Zoomed to selected warning's elements." : "Zoom failed, see the task dialog for the reason.";
            });
        }

        private void HighlightSelected()
        {
            if (SelectedWarning == null) return;

            List<ElementId> ids = SelectedWarning.AllElementIds.ToList();
            StatusText = "Highlighting...";
            HighlightWarningElementsHandler.Instance.RequestHighlight(ids, success =>
            {
                StatusText = success
                    ? "Highlighted selected warning's elements in the active view. Click Clear Highlights when done."
                    : "Highlight failed, see the task dialog for the reason.";
            });
        }

        private void ClearHighlights()
        {
            StatusText = "Clearing highlights...";
            HighlightWarningElementsHandler.Instance.RequestClear(success =>
            {
                StatusText = success ? "Highlights cleared." : "Clearing highlights failed, see log for details.";
            });
        }

        private void PreviewAutoResolve()
        {
            if (_settings.JoinResolutionRules.Count == 0)
            {
                StatusText = "No join resolution rules configured. Assign Join/Unjoin to a warning type below and save first.";
                return;
            }

            List<WarningItem> allWarnings = Groups.SelectMany(g => g.Items).ToList();

            DuplicateReviewPanelOpen = false;
            StatusText = "Building preview...";
            AutoResolveJoinsHandler.RequestPreview(allWarnings, _settings.JoinResolutionRules, preview =>
            {
                PreviewItems.Clear();
                foreach (JoinResolutionPreviewItem p in preview) PreviewItems.Add(p);

                PreviewPanelOpen = PreviewItems.Count > 0;
                StatusText = PreviewItems.Count == 0
                    ? "No warnings matched an active join resolution rule."
                    : $"{PreviewItems.Count(i => i.Include)} of {PreviewItems.Count} candidate pair(s) need action.";

                CommitAutoResolveCommand.RaiseCanExecuteChanged();
            });
        }

        private void CommitAutoResolve()
        {
            List<JoinResolutionPreviewItem> approved = PreviewItems.Where(i => i.Include).ToList();
            if (approved.Count == 0) return;

            StatusText = "Committing...";
            AutoResolveJoinsHandler.RequestCommit(approved, result =>
            {
                StatusText = $"Done. Succeeded: {result.Succeeded}, Failed: {result.Failed}, Skipped (stale): {result.SkippedStale}.";
                PreviewPanelOpen = false;
                PreviewItems.Clear();
                Refresh();
            });
        }

        private void ReviewDuplicates()
        {
            PreviewPanelOpen = false;
            StatusText = "Scanning for duplicate instances...";
            DuplicateInstanceReviewHandler.RequestPreview(_allWarnings, groups =>
            {
                DuplicateGroups.Clear();
                foreach (DuplicateInstanceGroup g in groups) DuplicateGroups.Add(g);

                DuplicateReviewPanelOpen = true;
                StatusText = DuplicateGroups.Count == 0
                    ? "No duplicate-instance warnings found."
                    : $"{DuplicateGroups.Count} duplicate group(s) found. Nothing is marked for deletion until you check it.";

                CommitDuplicateDeleteCommand.RaiseCanExecuteChanged();
            });
        }

        private void CommitDuplicateDelete()
        {
            List<DuplicateInstanceGroup> groups = DuplicateGroups.ToList();
            if (groups.Count == 0) return;

            StatusText = "Deleting...";
            DuplicateInstanceReviewHandler.RequestCommit(groups, result =>
            {
                StatusText = $"Done. Deleted {result.ElementsDeleted} element(s) across {result.GroupsProcessed} group(s). " +
                             $"Skipped (would have emptied a group): {result.GroupsSkippedWouldEmptyGroup}, " +
                             $"skipped (stale): {result.GroupsSkippedStale}, failed: {result.GroupsFailed}.";
                DuplicateReviewPanelOpen = false;
                DuplicateGroups.Clear();
                Refresh();
            });
        }

        public void Dispose()
        {
            _uiApp.Application.DocumentChanged -= OnDocumentChanged;
            _debounceTimer.Stop();
            HighlightWarningElementsHandler.Instance.RequestClear(null);
        }
    }
}