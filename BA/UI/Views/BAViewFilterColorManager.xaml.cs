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
        public static BAViewFilterColorManager Instance;
        public BAViewFilterColorManagerVm Vm { get; }

        public static BAViewFilterColorManager GetOrCreate(UIApplication uiApp, RevitExternalInvoker _invoker)
        {
            if (_instance == null || !_instance.IsLoaded)
                _instance = new BAViewFilterColorManager(uiApp, _invoker);
            return _instance;
        }

        private BAViewFilterColorManager(UIApplication uiApp, RevitExternalInvoker _invoker)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwnerToRevit(this, uiApp);
            Vm = new BAViewFilterColorManagerVm(uiApp, _invoker, this);
            DataContext = Vm;
            ContentRendered += (_, __) =>
            {
                Vm.EnsureTemplatesLoaded();
                Vm.EnsureParameterCategoriesLoaded(); // <- NEW
            };
            Closed += (_, __) =>
            {
                Vm.Dispose();
                _instance = null;
            };
        }
    }
}