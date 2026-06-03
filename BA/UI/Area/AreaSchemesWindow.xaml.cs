using System;
using Autodesk.Revit.UI;
using BA.UI.ExternalEvents;
using BA.UI.AreaSchemes;

namespace BA.UI.AreaSchemes
{
    public sealed partial class AreaSchemesWindow : System.Windows.Window
    {
        public static AreaSchemesWindow? Instance { get; private set; }

        public AreaSchemesWindow(UIApplication uiApp)
        {
            InitializeComponent();

            DataContext = new AreaSchemesViewModel(uiApp);

            Instance = this;
            Closed += (_, _) => Instance = null;
        }
    }
}