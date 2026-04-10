using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using BA.Core.Areas.EEH;
using BA.Core.Enums;
using BA.Core.Interfaces;
using BA.Core.Models;
using BA.Services.Parameters;
using RelayCommand = CommunityToolkit.Mvvm.Input;
using CtkRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;
using CtkAsyncRelayCommand = CommunityToolkit.Mvvm.Input.AsyncRelayCommand;

namespace BA.UI.ViewModels
{
    public sealed class AreaMainViewModel : ViewModelBase
    {
        // --------------------------------------------------------
        // Dependencies
        // --------------------------------------------------------
        private readonly CzaRevitBridge _bridge;
        private readonly IAreaComputationOrchestrator _orchestrator;
        private readonly IVisualizationEngine _visualizationEngine;
        private readonly ISharedParameterService _parameterService;
        private readonly UIApplication _uiApplication;

        // --------------------------------------------------------
        // State

        // --------------------------------------------------------
        private bool _isComputing;
        private string _statusMessage = "Připraven.";
        private string _progressText = string.Empty;
        private double _progressValue;
        private ResultRowViewModel? _selectedResult;

        private CancellationTokenSource? _currentCts;

        // --------------------------------------------------------
        // Observable collections
        // --------------------------------------------------------
        public ObservableCollection<AreaTypeViewModel> AreaTypes { get; } = new();
        public ObservableCollection<ResultRowViewModel> Results { get; } = new();

        // --------------------------------------------------------
        // Properties
        // --------------------------------------------------------
        public bool IsComputing
        {
            get => _isComputing;
            set
            {
                SetProperty(ref _isComputing, value);
                RaisePropertyChanged(nameof(IsNotComputing));
                ((CtkAsyncRelayCommand)ComputeCommand).NotifyCanExecuteChanged();
                ((CtkAsyncRelayCommand)VisualizeCommand).NotifyCanExecuteChanged();
                ((CtkAsyncRelayCommand)WriteParamsCommand).NotifyCanExecuteChanged();
            }
        }

        public bool IsNotComputing => !_isComputing;

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string ProgressText
        {
            get => _progressText;
            set => SetProperty(ref _progressText, value);
        }

        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public ResultRowViewModel? SelectedResult
        {
            get => _selectedResult;
            set => SetProperty(ref _selectedResult, value);
        }

        public string ResultSummary
        {
            get
            {
                if (!Results.Any()) return string.Empty;
                var total = Results.Where(r => r.IsSuccess).Sum(r =>
                {
                    double.TryParse(r.AreaM2.Replace(" m²", "")
                        .Replace(",", "."),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double v);
                    return v;
                });
                return $"Celkem: {Results.Count(r => r.IsSuccess)} výsledků";
            }
        }

        // --------------------------------------------------------
        // Commands
        // --------------------------------------------------------
        public System.Windows.Input.ICommand ComputeCommand { get; }
        public System.Windows.Input.ICommand CancelCommand { get; }
        public System.Windows.Input.ICommand VisualizeCommand { get; }
        public System.Windows.Input.ICommand ClearVizCommand { get; }
        public System.Windows.Input.ICommand WriteParamsCommand { get; }
        public System.Windows.Input.ICommand SelectAllCommand { get; }
        public System.Windows.Input.ICommand SelectNoneCommand { get; }

        // --------------------------------------------------------
        // Constructor
        // --------------------------------------------------------
        public AreaMainViewModel(
            CzaRevitBridge bridge,
                    IAreaComputationOrchestrator orchestrator,
            IVisualizationEngine visualizationEngine,
            ISharedParameterService parameterService,
            UIApplication uiApplication)
        {
            _bridge = bridge
                ?? throw new ArgumentNullException(nameof(bridge));
            _orchestrator = orchestrator
                ?? throw new ArgumentNullException(nameof(orchestrator));
            _visualizationEngine = visualizationEngine
                ?? throw new ArgumentNullException(nameof(visualizationEngine));
            _parameterService = parameterService
                ?? throw new ArgumentNullException(nameof(parameterService));
            _uiApplication = uiApplication
                ?? throw new ArgumentNullException(nameof(uiApplication));

            ComputeCommand = new AsyncRelayCommand(ComputeAsync,
                () => !IsComputing);
            CancelCommand = new CtkRelayCommand(CancelComputation,
                () => IsComputing);
            VisualizeCommand = new AsyncRelayCommand(VisualizeAsync,
                () => Results.Any(r => r.IsSuccess));
            ClearVizCommand = new AsyncRelayCommand(ClearVisualizationsAsync);
            WriteParamsCommand = new AsyncRelayCommand(WriteParametersAsync,
                () => Results.Any(r => r.IsSuccess));
            SelectAllCommand = new CtkRelayCommand(() =>
            {
                foreach (var t in AreaTypes) t.IsSelected = true;
            });
            SelectNoneCommand = new CtkRelayCommand(() =>
            {
                foreach (var t in AreaTypes) t.IsSelected = false;
            });

            InitializeAreaTypes();
        }

        // --------------------------------------------------------
        // Initialization
        // --------------------------------------------------------
        private void InitializeAreaTypes()
        {
            AreaTypes.Add(new AreaTypeViewModel
            {
                AreaType = AreaType.PodlahovaPlochaNV366,
                DisplayName = "Podlahová plocha",
                LegalCitation = "NV č. 366/2013 Sb. (ve znění NV č. 432/2022 Sb.)",
                ValidFrom = "1. 1. 2023",
                ScopeNote = null,
                IsSelected = true
            });

            AreaTypes.Add(new AreaTypeViewModel
            {
                AreaType = AreaType.HPPNadzemni,
                DisplayName = "HPP nadzemní",
                LegalCitation = "PSP Nař. HMP č. 10/2016 — §2 písm. c), §2 písm. g)",
                ValidFrom = "1. 1. 2024",
                ScopeNote = "Platí pouze v administrativních hranicích hl. m. Prahy",
                IsSelected = false
            });

            AreaTypes.Add(new AreaTypeViewModel
            {
                AreaType = AreaType.HPPPodzemni,
                DisplayName = "HPP podzemní",
                LegalCitation = "PSP Nař. HMP č. 10/2016 — §2 písm. c), §2 písm. g)",
                ValidFrom = "1. 1. 2024",
                ScopeNote = "Platí pouze v administrativních hranicích hl. m. Prahy",
                IsSelected = false
            });

            AreaTypes.Add(new AreaTypeViewModel
            {
                AreaType = AreaType.PodlahovaPlochaSZ,
                DisplayName = "Podlahová plocha (SZ)",
                LegalCitation = "Zákon č. 283/2021 Sb., stavební zákon — §13 písm. n)",
                ValidFrom = "1. 7. 2024",
                ScopeNote = null,
                IsSelected = false
            });

            AreaTypes.Add(new AreaTypeViewModel
            {
                AreaType = AreaType.ZastavenaPlochaSZ,
                DisplayName = "Zastavěná plocha",
                LegalCitation = "Zákon č. 283/2021 Sb., stavební zákon — §13 písm. o)",
                ValidFrom = "1. 7. 2024",
                ScopeNote = null,
                IsSelected = false
            });
        }

        // --------------------------------------------------------
        // Compute
        // --------------------------------------------------------
        private async Task ComputeAsync()
        {
            var selectedTypes = AreaTypes
                .Where(t => t.IsSelected)
                .Select(t => t.AreaType)
                .ToList();

            if (!selectedTypes.Any())
            {
                StatusMessage = "Vyberte alespoň jeden typ plochy.";
                return;
            }

            Results.Clear();
            IsComputing = true;
            ProgressValue = 0;
            StatusMessage = "Zahajuji výpočet...";

            _currentCts = new CancellationTokenSource();
            var token = _currentCts.Token;

            try
            {
                // Sběr Room IDs na main Revit threadu
                var elementIds = await _bridge.ExecuteAsync(app =>
                    CollectRoomIds(app.ActiveUIDocument.Document));

                if (!elementIds.Any())
                {
                    StatusMessage = "V modelu nebyly nalezeny žádné umístěné místnosti.";
                    return;
                }

                StatusMessage = $"Zpracovávám {elementIds.Count} místností...";

                var results = await _bridge.ExecuteAsync(app =>
                {
                    // Výpočet probíhá synchronně na main threadu
                    // (Revit API není thread-safe)
                    return _orchestrator.ComputeAsync(
                        selectedTypes,
                        elementIds,
                        app,
                        token).GetAwaiter().GetResult();
                });

                foreach (var result in results)
                    Results.Add(new ResultRowViewModel { Result = result });

                int successCount = Results.Count(r => r.IsSuccess);
                int skipCount = Results.Count(r => !r.IsSuccess);

                StatusMessage = $"Hotovo — {successCount} výsledků" +
                    (skipCount > 0 ? $", {skipCount} přeskočeno" : string.Empty) + ".";

                RaisePropertyChanged(nameof(IsNotComputing));
                ((CtkAsyncRelayCommand)ComputeCommand).NotifyCanExecuteChanged();
                ((CtkAsyncRelayCommand)VisualizeCommand).NotifyCanExecuteChanged();
                ((CtkAsyncRelayCommand)WriteParamsCommand).NotifyCanExecuteChanged();
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Výpočet byl zrušen.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba: {ex.Message}";
            }
            finally
            {
                IsComputing = false;
                ProgressValue = 0;
                ProgressText = string.Empty;
                _currentCts?.Dispose();
                _currentCts = null;
            }
        }

        private void CancelComputation()
        {
            _currentCts?.Cancel();
            StatusMessage = "Ruším výpočet...";
        }

        // --------------------------------------------------------
        // Visualize
        // --------------------------------------------------------
        private async Task VisualizeAsync()
        {
            StatusMessage = "Vykreslování...";

            try
            {
                var resultsToVisualize = Results
                    .Where(r => r.IsSuccess)
                    .Select(r => r.Result)
                    .ToList();

                await _bridge.ExecuteAsync(app =>
                {
                    var doc = app.ActiveUIDocument.Document;
                    var view = app.ActiveUIDocument.ActiveView;

                    using var tx = new Transaction(doc, "CZA — Vizualizace ploch");
                    tx.Start();
                    _visualizationEngine.VisualizeResults(resultsToVisualize, view, doc);
                    tx.Commit();
                });

                StatusMessage = "Výsledky byly vykresleny v aktivním pohledu.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba vizualizace: {ex.Message}";
            }
        }

        // --------------------------------------------------------
        // Clear visualizations
        // --------------------------------------------------------
        private async Task ClearVisualizationsAsync()
        {
            try
            {
                await _bridge.ExecuteAsync(app =>
                {
                    var doc = app.ActiveUIDocument.Document;
                    using var tx = new Transaction(doc, "CZA — Smazání vizualizací");
                    tx.Start();
                    _visualizationEngine.ClearVisualizations(null, doc);
                    tx.Commit();
                });

                StatusMessage = "Vizualizace byly odstraněny.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba mazání: {ex.Message}";
            }
        }

        // --------------------------------------------------------
        // Write parameters
        // --------------------------------------------------------
        private async Task WriteParametersAsync()
        {
            StatusMessage = "Zapisuji do sdílených parametrů...";

            try
            {
                var resultsToWrite = Results
                    .Where(r => r.IsSuccess)
                    .Select(r => r.Result)
                    .ToList();

                await _bridge.ExecuteAsync(app =>
                {
                    var doc = app.ActiveUIDocument.Document;
                    using var tx = new Transaction(doc, "CZA — Zápis parametrů");
                    tx.Start();
                    _parameterService.WriteResults(resultsToWrite, doc);
                    tx.Commit();
                });

                StatusMessage = "Výsledky byly zapsány do sdílených parametrů.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba zápisu: {ex.Message}";
            }
        }

        // --------------------------------------------------------
        // Helpers
        // --------------------------------------------------------
        private static List<ElementId> CollectRoomIds(Document document)
        {
            return new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0.01)
                .Select(r => r.Id)
                .ToList();
        }
    }
}