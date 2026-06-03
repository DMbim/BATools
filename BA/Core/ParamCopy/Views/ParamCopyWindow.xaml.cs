using Autodesk.Revit.UI;
using BATools.ParamCopy.ViewModels;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WpfBinding = System.Windows.Data.Binding;

namespace BATools.ParamCopy.Views
{
    public partial class ParamCopyWindow : Window
    {
        private static ParamCopyWindow? _instance;

        // Track which column headers were added dynamically so we can remove them on reload.
        private readonly HashSet<string> _sourceDynamicHeaders = new();
        private readonly HashSet<string> _destDynamicHeaders = new();

        public static void ShowOrFocus(UIApplication uiApp)
        {
            if (_instance == null || !_instance.IsLoaded)
            {
                _instance = new ParamCopyWindow(uiApp);
                new System.Windows.Interop.WindowInteropHelper(_instance).Owner =
                    uiApp.MainWindowHandle;
                _instance.Show();
            }
            else
            {
                if (_instance.WindowState == WindowState.Minimized)
                    _instance.WindowState = WindowState.Normal;
                _instance.Activate();
                _instance.Focus();
            }
        }

        private ParamCopyWindow(UIApplication uiApp)
        {
            InitializeComponent();
            var vm = new ParamCopyViewModel(uiApp);
            DataContext = vm;

            vm.SourceColumnsChanged += names =>
                RebuildColumns(SourceGrid, names, _sourceDynamicHeaders);
            vm.DestColumnsChanged += names =>
                RebuildColumns(DestGrid, names, _destDynamicHeaders);
        }

        /// <summary>
        /// Removes previously added dynamic columns then adds one column per
        /// display parameter. Dynamic columns are tracked by header string in
        /// the provided HashSet — DataGridColumn has no Tag property.
        /// </summary>
        private static void RebuildColumns(
            DataGrid grid,
            IReadOnlyList<string> paramNames,
            HashSet<string> trackedHeaders)
        {
            // Remove all previously tracked dynamic columns
            for (int i = grid.Columns.Count - 1; i >= 0; i--)
            {
                if (grid.Columns[i].Header is string h && trackedHeaders.Contains(h))
                    grid.Columns.RemoveAt(i);
            }
            trackedHeaders.Clear();

            // Add one column per display parameter
            foreach (var paramName in paramNames)
            {
                var col = new DataGridTextColumn
                {
                    Header = paramName,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    IsReadOnly = true,
                    Binding = new WpfBinding($"ParameterValues[{paramName}]")
                    {
                        FallbackValue = string.Empty,
                        TargetNullValue = string.Empty
                    }
                };

                grid.Columns.Add(col);
                trackedHeaders.Add(paramName);
            }
        }
    }
}