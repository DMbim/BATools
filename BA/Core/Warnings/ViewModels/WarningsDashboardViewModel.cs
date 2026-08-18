// FILE: BA_Tools/Warnings/ViewModels/WarningsDashboardViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Warnings.ExternalEvents;
using BA.Warnings.Models;
using BA.Warnings.Settings;

namespace BA.Warnings.ViewModels
{
    public sealed class WarningGroupViewModel : BA.UI.Mvvm.ObservableObject
    {
        public FailureSeverity Severity { get; }
        public ObservableCollection<WarningItem> Items { get; } = new ObservableCollection<WarningItem>();

        public string Header => $"{Severity} ({Items.Count})";

        public WarningGroupViewModel(FailureSeverity severity)
        {
            Severity = severity;
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

        public ObservableCollection<WarningGroupViewModel> Groups { get; } = new ObservableCollection<WarningGroupViewModel>();
        public ObservableCollection<JoinRuleRowViewModel> Rules { get; } = new ObservableCollection<JoinRuleRowViewModel>();
        public ObservableCollection<JoinResolutionPreviewItem> PreviewItems { get; } = new ObservableCollection<JoinResolutionPreviewItem>();

        private WarningItem _selectedWarning;
        public WarningItem SelectedWarning
        {
            get => _selectedWarning;
            set
            {
                _selectedWarning = value;
                OnPropertyChanged(nameof(SelectedWarning));
                ZoomToSelectedCommand.RaiseCanExecuteChanged();
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

        private string _statusText = "Ready.";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public BA.UI.Mvvm.RelayCommand RefreshCommand { get; }
        public BA.UI.Mvvm.RelayCommand ZoomToSelectedCommand { get; }
        public BA.UI.Mvvm.RelayCommand PreviewAutoResolveCommand { get; }
        public BA.UI.Mvvm.RelayCommand CommitAutoResolveCommand { get; }
        public BA.UI.Mvvm.RelayCommand SaveRulesCommand { get; }
        public BA.UI.Mvvm.RelayCommand ClosePreviewCommand { get; }

        public WarningsDashboardViewModel(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _settings = WarningsDashboardSettings.Load<WarningsDashboardSettings>() ?? new WarningsDashboardSettings();
            _settings.SeedDefaultJoinRulesIfNeeded();

            foreach (FailureSeverity sev in new[] { FailureSeverity.Warning, FailureSeverity.Error, FailureSeverity.DocumentCorruption })
            {
                Groups.Add(new WarningGroupViewModel(sev));
            }

            RefreshCommand = new BA.UI.Mvvm.RelayCommand(_ => Refresh());
            ZoomToSelectedCommand = new BA.UI.Mvvm.RelayCommand(_ => ZoomToSelected(), _ => SelectedWarning != null);
            PreviewAutoResolveCommand = new BA.UI.Mvvm.RelayCommand(_ => PreviewAutoResolve());
            CommitAutoResolveCommand = new BA.UI.Mvvm.RelayCommand(_ => CommitAutoResolve(), _ => PreviewItems.Any(i => i.Include));
            SaveRulesCommand = new BA.UI.Mvvm.RelayCommand(_ => SaveRules());
            ClosePreviewCommand = new BA.UI.Mvvm.RelayCommand(_ => PreviewPanelOpen = false);

            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                Refresh();
            };

            _uiApp.Application.DocumentChanged += OnDocumentChanged;

            Refresh();
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
            RefreshWarningsHandler.Instance.RequestRefresh(items =>
            {
                foreach (WarningGroupViewModel group in Groups)
                {
                    group.Refresh(items.Where(i => i.Severity == group.Severity));
                }

                RebuildRuleRows(items);

                StatusText = $"{items.Count} warning(s) loaded.";
            });
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

        private void ZoomToSelected()
        {
            if (SelectedWarning == null) return;

            List<ElementId> ids = SelectedWarning.AllElementIds.ToList();
            StatusText = "Zooming...";
            ZoomToWarningElementsHandler.Instance.RequestZoom(ids, success =>
            {
                StatusText = success ? "Zoomed to selected warning's elements." : "Zoom failed, see the task dialog for the reason.";
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

            StatusText = "Building preview...";
            AutoResolveJoinsHandler.Instance.RequestPreview(allWarnings, _settings.JoinResolutionRules, preview =>
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
            AutoResolveJoinsHandler.Instance.RequestCommit(approved, result =>
            {
                StatusText = $"Done. Succeeded: {result.Succeeded}, Failed: {result.Failed}, Skipped (stale): {result.SkippedStale}.";
                PreviewPanelOpen = false;
                PreviewItems.Clear();
                Refresh();
            });
        }

        public void Dispose()
        {
            _uiApp.Application.DocumentChanged -= OnDocumentChanged;
            _debounceTimer.Stop();
        }
    }
}