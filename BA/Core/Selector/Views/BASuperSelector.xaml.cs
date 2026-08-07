// File: BA.UI/Views/BASuperSelector.xaml.cs
using Autodesk.Revit.UI;
using BA.UI.ExternalEvents;
using BA.UI.Helpers;
using System;
using System.Windows;

namespace BA.UI.Views
{
    public partial class BASuperSelector : Window
    {
        private static BASuperSelector _instance;
        public static BASuperSelector Instance;
        public BASuperSelectorVm Vm { get; }

        public static BASuperSelector GetOrCreate(UIApplication uiApp, RevitExternalInvoker invoker)
        {
            if (_instance == null || !_instance.IsLoaded)
                _instance = new BASuperSelector(uiApp, invoker);
            return _instance;
        }

        private BASuperSelector(UIApplication uiApp, RevitExternalInvoker invoker)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwnerToRevit(this, uiApp);
            Vm = new BASuperSelectorVm(uiApp, invoker, this);
            DataContext = Vm;
            ContentRendered += (_, __) => Vm.EnsureCategoriesLoaded();
            Closed += (_, __) =>
            {
                Vm.Dispose();
                _instance = null;
            };
        }
    }
}