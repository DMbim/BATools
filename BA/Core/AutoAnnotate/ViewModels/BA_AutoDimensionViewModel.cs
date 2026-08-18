using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using BA.UI.Mvvm;
using BA.BIM.Core.Dimensioning.Infrastructure;
using BA.BIM.Core.Dimensioning.Models;
using BA.BIM.Core.Dimensioning.Services;
using BA.BIM.Core.ViewScoping;

namespace BA.BIM.Commands.Dimension
{
    public sealed class BA_AutoDimensionViewModel : BA.UI.Mvvm.ObservableObject
    {
        private readonly UIDocument _uiDoc;
        private readonly BA_DimensionRevitBridge _bridge;

        public ObservableCollection<BA_DimensionCandidateVM> Candidates { get; } = new ObservableCollection<BA_DimensionCandidateVM>();
        public ObservableCollection<BA_DimensionSkip> Skips { get; } = new ObservableCollection<BA_DimensionSkip>();
        public ObservableCollection<BA_DimensionPlacementOutcome> Outcomes { get; } = new ObservableCollection<BA_DimensionPlacementOutcome>();

        public BA_ViewScopeMode[] ViewScopeModes { get; } =
            { BA_ViewScopeMode.ActiveViewOnly, BA_ViewScopeMode.AllFloorPlans };

        private BA_ViewScopeMode _viewScopeMode = BA_ViewScopeMode.ActiveViewOnly;
        public BA_ViewScopeMode ViewScopeMode
        {
            get => _viewScopeMode;
            set => SetProperty(ref _viewScopeMode, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        private string _statusMessage = "Ready.";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private int _selectedCount;
        public int SelectedCount
        {
            get => _selectedCount;
            set => SetProperty(ref _selectedCount, value);
        }

        public BA.UI.Mvvm.AsyncRelayCommand ScanCommand { get; }
        public BA.UI.Mvvm.AsyncRelayCommand PlaceCommand { get; }

        public BA_AutoDimensionViewModel(UIDocument uiDoc, BA_DimensionRevitBridge bridge)
        {
            _uiDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));

            ScanCommand = new BA.UI.Mvvm.AsyncRelayCommand(ScanAsync, () => !IsBusy);
            PlaceCommand = new BA.UI.Mvvm.AsyncRelayCommand(PlaceAsync, () => !IsBusy && Candidates.Any(c => c.IsSelected));
        }

        private async Task ScanAsync()
        {
            IsBusy = true;
            StatusMessage = "Scanning...";
            Candidates.Clear();
            Skips.Clear();
            Outcomes.Clear();

            try
            {
                BA_ViewScopeMode scopeMode = ViewScopeMode;

                (List<BA_DimensionCandidate> candidates, List<BA_DimensionSkip> skips) = await _bridge.RunAsync(uiApp =>
                {
                    var doc = uiApp.ActiveUIDocument.Document;
                    var views = BA_ViewScopeResolver.Resolve(doc, scopeMode, uiApp.ActiveUIDocument.ActiveView, null);

                    var allCandidates = new List<BA_DimensionCandidate>();
                    var allSkips = new List<BA_DimensionSkip>();

                    foreach (var view in views)
                    {
                        var (c, s) = BA_DimensionCandidateService.ScanView(doc, view);
                        allCandidates.AddRange(c);
                        allSkips.AddRange(s);
                    }

                    return (allCandidates, allSkips);
                });

                foreach (var c in candidates)
                {
                    var vm = new BA_DimensionCandidateVM(c);
                    vm.PropertyChanged += (_, __) =>
                    {
                        UpdateSelectedCount();
                        PlaceCommand.RaiseCanExecuteChanged();
                    };
                    Candidates.Add(vm);
                }

                foreach (var s in skips)
                    Skips.Add(s);

                StatusMessage = $"Scan complete: {candidates.Count} candidate wall(s), {skips.Count} skipped.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Scan failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                UpdateSelectedCount();
                ScanCommand.RaiseCanExecuteChanged();
                PlaceCommand.RaiseCanExecuteChanged();
            }
        }

        private async Task PlaceAsync()
        {
            var selected = Candidates.Where(c => c.IsSelected).Select(c => c.Model).ToList();
            if (selected.Count == 0)
            {
                StatusMessage = "No candidates selected.";
                return;
            }

            IsBusy = true;
            StatusMessage = "Placing dimensions...";
            Outcomes.Clear();

            try
            {
                (List<BA_DimensionPlacementOutcome> outcomes, List<BA_DimensionSkip> runtimeSkips) = await _bridge.RunAsync(uiApp =>
                {
                    var doc = uiApp.ActiveUIDocument.Document;
                    return BA_DimensionPlacementService.Execute(doc, selected);
                });

                foreach (var o in outcomes)
                    Outcomes.Add(o);
                foreach (var s in runtimeSkips)
                    Skips.Add(s);

                int placed = outcomes.Count(o => o.Success);
                int failed = outcomes.Count(o => !o.Success);
                StatusMessage = $"Placement complete: {placed} placed, {failed} failed.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Placement failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                ScanCommand.RaiseCanExecuteChanged();
                PlaceCommand.RaiseCanExecuteChanged();
            }
        }

        private void UpdateSelectedCount() => SelectedCount = Candidates.Count(c => c.IsSelected);
    }
}