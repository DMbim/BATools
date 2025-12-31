using Autodesk.Revit.UI;
using BA.UI.ExternalEvents;
using BA.UI.Helpers;
using System;
using System.Windows;

namespace BA.UI.Views
{
    public partial class BAViewFilterColorManager : Window
    {
        private static BAViewFilterColorManager _instance;

        public BAViewFilterColorManagerVm Vm { get; }

        public static BAViewFilterColorManager GetOrCreate(UIApplication uiApp, RevitExternalInvoker revit)
        {
            if (_instance == null || !_instance.IsLoaded)
                _instance = new BAViewFilterColorManager(uiApp, revit);

            return _instance;
        }

        private BAViewFilterColorManager(UIApplication uiApp, RevitExternalInvoker revit)
        {
            InitializeComponent();

            RevitWindowHelper.SetOwnerToRevit(this, uiApp);

            Vm = new BAViewFilterColorManagerVm(uiApp, revit, this);
            DataContext = Vm;

            ContentRendered += (_, __) => Vm.EnsureTemplatesLoaded();

            Closed += (_, __) =>
            {
                Vm.Dispose();
                _instance = null;
            };
        }
    }
}
