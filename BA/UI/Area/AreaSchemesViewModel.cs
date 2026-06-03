using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.AreaSchemes.Constants;
using BA.Core.AreaSchemes.Models;
using BA.Core.AreaSchemes.Services;
using BA.UI.ExternalEvents;
using BA.UI.Helpers;
using CtkRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BA.UI.AreaSchemes
{
    public sealed class AreaSchemesViewModel : ObservableObject
    {
        private readonly UIApplication _uiApp;

        // --------------------------------------------------------
        // Observable state
        // --------------------------------------------------------
        public ObservableCollection<Level> Levels { get; } = new();

        private Level? _selectedLevel;
        public Level? SelectedLevel
        {
            get => _selectedLevel;
            set
            {
                SetProperty(ref _selectedLevel, value);
                OnLevelSelected();
            }
        }

        private int _currentStepIndex;
        public int CurrentStepIndex
        {
            get => _currentStepIndex;
            set
            {
                SetProperty(ref _currentStepIndex, value);
                OnPropertyChanged(nameof(CurrentStep));
                OnPropertyChanged(nameof(CurrentStepNumber));
                OnPropertyChanged(nameof(TotalSteps));
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(IsLastStep));
                RefreshCurrentStepState();
            }
        }

        public string CurrentStep => CurrentStepIndex < _definitions.Length
            ? _definitions[CurrentStepIndex].SchemeName
            : string.Empty;

        public int CurrentStepNumber => CurrentStepIndex + 1;
        public int TotalSteps => _definitions.Length;
        public bool CanGoBack => CurrentStepIndex > 0;
        public bool CanGoNext => CurrentStepIndex < _definitions.Length - 1;
        public bool IsLastStep => CurrentStepIndex == _definitions.Length - 1;

        private AreaLevelState? _currentState;
        public AreaLevelState? CurrentState
        {
            get => _currentState;
            set => SetProperty(ref _currentState, value);
        }

        private string _statusMessage = "Vyberte podlaží pro zahájení.";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }
        private void RefreshCommands()
        {
            ((CtkRelayCommand)NextCommand).NotifyCanExecuteChanged();
            ((CtkRelayCommand)BackCommand).NotifyCanExecuteChanged();
            ((CtkRelayCommand)CreateViewCommand).NotifyCanExecuteChanged();
            ((CtkRelayCommand)OpenViewCommand).NotifyCanExecuteChanged();
            ((CtkRelayCommand)PickElementsCommand).NotifyCanExecuteChanged();
            ((CtkRelayCommand)FinishCommand).NotifyCanExecuteChanged();
        }
        // --------------------------------------------------------
        // Commands
        // --------------------------------------------------------
        public ICommand NextCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand CreateViewCommand { get; }
        public ICommand OpenViewCommand { get; }
        public ICommand PickElementsCommand { get; }
        public ICommand FinishCommand { get; }

        // --------------------------------------------------------
        // Definitions
        // --------------------------------------------------------
        private static readonly AreaSchemeDefinition[] _definitions =
        {
            new() { Step = AreaSchemeStep.LA,  SchemeName = AreaSchemeConstants.LA,
                    Code = "LA",  DisplayName = "Level Area",
                    StepType = AreaSchemeStepType.UserDrawn,
                    ResultParamName = AreaSchemeConstants.ParamLA,
                    Description = "Celková plocha podlaží měřená k vnějšímu líci." },

            new() { Step = AreaSchemeStep.NLA, SchemeName = AreaSchemeConstants.NLA,
                    Code = "NLA", DisplayName = "Non-Functional Level Area",
                    StepType = AreaSchemeStepType.UserDrawn,
                    ResultParamName = AreaSchemeConstants.ParamNLA,
                    Description = "Plocha atrií, prostupů a nevyužitelných prostor." },

            new() { Step = AreaSchemeStep.GFA, SchemeName = AreaSchemeConstants.GFA,
                    Code = "GFA", DisplayName = "Gross Floor Area",
                    StepType = AreaSchemeStepType.Computed,
                    ResultParamName = AreaSchemeConstants.ParamGFA,
                    Description = "LA − NLA = GFA" },

            new() { Step = AreaSchemeStep.ECA, SchemeName = AreaSchemeConstants.ECA,
                    Code = "ECA", DisplayName = "Exterior Construction Area",
                    StepType = AreaSchemeStepType.ElementPick,
                    AreaTypeTag = AreaSchemeConstants.AreaTypeECA,
                    ResultParamName = AreaSchemeConstants.ParamECA,
                    Description = "Plocha obvodových konstrukcí — vyberte obvodové stěny." },

            new() { Step = AreaSchemeStep.IFA, SchemeName = AreaSchemeConstants.IFA,
                    Code = "IFA", DisplayName = "Internal Floor Area",
                    StepType = AreaSchemeStepType.Computed,
                    ResultParamName = AreaSchemeConstants.ParamIFA,
                    Description = "GFA − ECA = IFA" },

            new() { Step = AreaSchemeStep.ICA, SchemeName = AreaSchemeConstants.ICA,
                    Code = "ICA", DisplayName = "Interior Construction Area",
                    StepType = AreaSchemeStepType.ElementPick,
                    AreaTypeTag = AreaSchemeConstants.AreaTypeICA,
                    ResultParamName = AreaSchemeConstants.ParamICA,
                    Description = "Plocha nosných stěn a sloupů — vyberte nosné konstrukce." },

            new() { Step = AreaSchemeStep.NFA, SchemeName = AreaSchemeConstants.NFA,
                    Code = "NFA", DisplayName = "Net Floor Area",
                    StepType = AreaSchemeStepType.Computed,
                    ResultParamName = AreaSchemeConstants.ParamNFA,
                    Description = "IFA − ICA = NFA" },

            new() { Step = AreaSchemeStep.PWA, SchemeName = AreaSchemeConstants.PWA,
                    Code = "PWA", DisplayName = "Partition Wall Area",
                    StepType = AreaSchemeStepType.ElementPick,
                    AreaTypeTag = AreaSchemeConstants.AreaTypePWA,
                    ResultParamName = AreaSchemeConstants.ParamPWA,
                    Description = "Plocha dělících konstrukcí — vyberte příčky." },

            new() { Step = AreaSchemeStep.NRA, SchemeName = AreaSchemeConstants.NRA,
                    Code = "NRA", DisplayName = "Net Room Area",
                    StepType = AreaSchemeStepType.Computed,
                    ResultParamName = AreaSchemeConstants.ParamNRA,
                    Description = "NFA − PWA = NRA" },
        };

        // --------------------------------------------------------
        // Constructor
        // --------------------------------------------------------
        public AreaSchemesViewModel(UIApplication uiApp)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));

            NextCommand = new CtkRelayCommand(GoNext, () => CanGoNext && !IsBusy);
            BackCommand = new CtkRelayCommand(GoBack, () => CanGoBack && !IsBusy);
            CreateViewCommand = new CtkRelayCommand(CreateView, () => _selectedLevel != null && !IsBusy);
            OpenViewCommand = new CtkRelayCommand(OpenView, () => CurrentState?.ViewId != null && !IsBusy);
            PickElementsCommand = new CtkRelayCommand(PickElements, () => CanPickElements && !IsBusy);
            FinishCommand = new CtkRelayCommand(Finish, () => IsLastStep && !IsBusy);

            LoadLevels();
        }

        // --------------------------------------------------------
        // Properties derived from current step
        // --------------------------------------------------------
        public AreaSchemeDefinition? CurrentDefinition =>
            CurrentStepIndex < _definitions.Length
                ? _definitions[CurrentStepIndex]
                : null;

        public bool CanPickElements =>
            CurrentDefinition?.StepType == AreaSchemeStepType.ElementPick;

        public bool IsComputed =>
            CurrentDefinition?.StepType == AreaSchemeStepType.Computed;

        // --------------------------------------------------------
        // Load levels
        // --------------------------------------------------------
        private void LoadLevels()
        {
            var levels = AreaSchemeViewService.GetLevels(
                _uiApp.ActiveUIDocument.Document);

            Levels.Clear();
            foreach (var l in levels)
                Levels.Add(l);
        }

        // --------------------------------------------------------
        // Level selected
        // --------------------------------------------------------
        private void OnLevelSelected()
        {
            if (_selectedLevel == null) return;
            CurrentStepIndex = 0;
            // Defer refresh so the UI thread is free to process the dispatcher callback
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(RefreshCurrentStepState));
            RefreshCommands();
            // TEMP DEBUG
            var doc = _uiApp.ActiveUIDocument.Document;
            var schemes = new FilteredElementCollector(doc)
                .OfClass(typeof(AreaScheme))
                .Cast<AreaScheme>()
                .ToList();

            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => v.ViewType == ViewType.AreaPlan)
                .ToList();


        }

        // --------------------------------------------------------
        // Step navigation
        // --------------------------------------------------------
        private void GoNext()
        {
            if (CanGoNext)
                CurrentStepIndex++;
            RefreshCommands();
        }

        private void GoBack()
        {
            if (CanGoBack)
                CurrentStepIndex--;
            RefreshCommands();
        }

        // --------------------------------------------------------
        // Refresh state for current step
        // --------------------------------------------------------
        private void RefreshCurrentStepState()
        {
            if (_selectedLevel == null || CurrentDefinition == null) return;

            var doc = _uiApp.ActiveUIDocument.Document;
            var state = AreaSchemeViewService.GetLevelState(
                doc, _selectedLevel, CurrentDefinition);

            CurrentState = state;
            StatusMessage = BuildStatusMessage(state);
            IsBusy = false;
            OnPropertyChanged(nameof(CanPickElements));
            RefreshCommands();
        }

        // --------------------------------------------------------
        // Open view in Revit
        // --------------------------------------------------------
        private void OpenView()
        {
            if (CurrentState?.ViewId == null) return;

            var doc = _uiApp.ActiveUIDocument.Document;
            var view = doc.GetElement(CurrentState.ViewId) as ViewPlan;
            if (view == null) return;

            AreaSchemeViewService.ActivateView(_uiApp.ActiveUIDocument, view);
        }
        private void CreateView()
        {
            if (_selectedLevel == null || CurrentDefinition == null) return;

            var doc = _uiApp.ActiveUIDocument.Document;

            using var tx = new Transaction(doc, $"BA — Create {CurrentDefinition.Code} View");
            tx.Start();
            var (view, wasCreated) = AreaSchemeViewService.EnsureAreaPlanView(
                doc, _selectedLevel, CurrentDefinition.SchemeName);
            tx.Commit();

            StatusMessage = wasCreated
                ? $"View created: {view.Name}"
                : $"View already exists: {view.Name}";

            RefreshCurrentStepState();
            RefreshCommands();
        }
        // --------------------------------------------------------
        // Pick elements
        // --------------------------------------------------------
        private void PickElements()
        {
            if (CurrentDefinition == null || _selectedLevel == null) return;
            if (CurrentDefinition.AreaTypeTag == null) return;

            StatusMessage =
                $"Vyberte prvky pro {CurrentDefinition.Code} v aktivním pohledu, " +
                $"pak klikněte 'Dokončit výběr' (Finish) v Revitu.";

            // Selection happens in Revit view — user selects, then
            // we process on next button press or a dedicated "Apply Selection" button.
            // For now flag that we're in pick mode.
        }

        // --------------------------------------------------------
        // Finish
        // --------------------------------------------------------
        private void Finish()
        {
            if (_selectedLevel == null) return;

            var doc = _uiApp.ActiveUIDocument.Document;

            var viewsByScheme = AreaSchemeConstants.OrderedSchemes
                .ToDictionary(
                    s => s,
                    s => AreaSchemeViewService.FindAreaPlanView(
                        doc, _selectedLevel, s));

            var result = AreaValueService.ComputeResult(
                doc, _selectedLevel, viewsByScheme!);

            using var tx = new Transaction(doc, "BA — Zápis výsledků ploch");
            tx.Start();
            AreaValueService.WriteToLevel(doc, result);
            tx.Commit();

            StatusMessage =
                $"Done.  " +
                $"LA={result.LA:F2}  NLA={result.NLA:F2}  " +
                $"GFA={result.GFA:F2}  ECA={result.ECA:F2}  " +
                $"IFA={result.IFA:F2}  ICA={result.ICA:F2}  " +
                $"NFA={result.NFA:F2}  PWA={result.PWA:F2}  " +
                $"NRA={result.NRA:F2}";
            IsBusy = false;
        }

        // --------------------------------------------------------
        // Helpers
        // --------------------------------------------------------
        private static string BuildStatusMessage(AreaLevelState state)
        {
            return state.ViewStatus switch
            {
                ViewStatus.Missing =>
                    $"Pohled neexistuje. Bude vytvořen při prvním otevření.",
                ViewStatus.ExistsEmpty =>
                    $"Pohled existuje ale neobsahuje žádné plochy.",
                ViewStatus.ExistsWithAreas =>
                    $"Nalezeno {state.AreaCount} ploch, " +
                    $"celkem {state.AreaM2:F2} m².",
                _ => string.Empty
            };
        }
    }
}