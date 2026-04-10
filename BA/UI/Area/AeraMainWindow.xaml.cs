// BA/UI/Views/AreaMainWindow.xaml.cs
using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using BA.Core.Areas.EEH;
using BA.Core.Enums;
using BA.Core.Interfaces;
using BA.Services;
using BA.Services.Computation;
using BA.Services.Geometry;
using BA.Services.Parameters;
using BA.Services.Visualization;
using BA.UI.ViewModels;

namespace BA.UI.Views
{
    public sealed partial class AreaMainWindow : System.Windows.Window
    {
        public static AreaMainWindow? Instance { get; private set; }

        public AreaMainWindow(CzaRevitBridge bridge, UIApplication uiApplication)
        {


            var normInfoProvider = new NormInfoProvider();
            var geometryEngine = new GeometryEngine();
            var heightZoneAnalyzer = new HeightZoneAnalyzer(geometryEngine);
            var hppClassifier = new HPPClassifier();

            var strategies = new Dictionary<AreaType, IAreaComputationStrategy>
            {
                {
                    AreaType.PodlahovaPlochaNV366,
                    new PodlahovaPlochaNV366Strategy(
                        geometryEngine, heightZoneAnalyzer, normInfoProvider)
                },
                {
                    AreaType.HPPNadzemni,
                    new HPPStrategy(
                        AreaType.HPPNadzemni,
                        geometryEngine, hppClassifier, normInfoProvider)
                },
                {
                    AreaType.HPPPodzemni,
                    new HPPStrategy(
                        AreaType.HPPPodzemni,
                        geometryEngine, hppClassifier, normInfoProvider)
                },
                {
                    AreaType.PodlahovaPlochaSZ,
                    new PodlahovaPlochaSZStrategy(geometryEngine, normInfoProvider)
                },
                {
                    AreaType.ZastavenaPlochaSZ,
                    new ZastavenaPlochaSZStrategy(geometryEngine, normInfoProvider)
                }
            };

            var orchestrator = new AreaComputationOrchestrator(strategies);
            var visualizationEngine = new VisualizationEngine();
            var parameterService = new SharedParameterService();

            DataContext = new AreaMainViewModel(
                bridge,
                orchestrator,
                visualizationEngine,
                parameterService,
                uiApplication);

            Instance = this;
            Closed += (_, _) => Instance = null;
        }
    }
}