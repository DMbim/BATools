using System;
using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.UI;
using BA.Core.Content.Models;

namespace BA.UI.LoadedFamilyBrowser
{
    public partial class LoadedFamilyBrowserWindow : Window
    {
        private readonly LoadedFamilyBrowserViewModel _viewModel;

        public LoadedFamilyBrowserWindow(UIApplication uiApp, IReadOnlyList<ContentItem> libraryIndex)
        {
            InitializeComponent();

            IntPtr ownerHandle = uiApp.MainWindowHandle;
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            helper.Owner = ownerHandle;

            _viewModel = new LoadedFamilyBrowserViewModel(uiApp, ownerHandle, libraryIndex);
            DataContext = _viewModel;

            Closed += (_, _) => _viewModel.Dispose();
        }

        private void FamilyTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _viewModel.SelectedNode = e.NewValue;
        }
    }
}