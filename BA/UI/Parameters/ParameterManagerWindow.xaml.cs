using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Parameters;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace BA.UI.Parameters
{
    public partial class ParameterManagerWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly Document _doc;

        private readonly ObservableCollection<ParameterRow> _rows = new();
        private string _search = string.Empty;

        public ParameterManagerWindow(UIApplication uiApp, Document doc)
        {
            InitializeComponent();

            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            TxtSpPath.Text = _uiApp.Application.SharedParametersFilename ?? string.Empty;

            GridParams.ItemsSource = _rows;
            Reload();
        }

        private void Reload()
        {
            _rows.Clear();

            var all = ParameterBindingCollector.Collect(_doc)
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var r in all)
                _rows.Add(r);

            ApplySearch();
        }

        private void ApplySearch()
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(GridParams.ItemsSource);
            if (view == null) return;

            var s = (_search ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
            {
                view.Filter = null;
                return;
            }

            view.Filter = obj =>
            {
                if (obj is not ParameterRow row) return false;
                return row.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                       || (row.CategoriesCsv?.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
            };
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => Reload();

        private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _search = TxtSearch.Text ?? string.Empty;
            ApplySearch();
        }

        private void BtnBrowseSp_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Shared Parameter File",
                Filter = "TXT files (*.txt)|*.txt|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dlg.ShowDialog() == true)
            {
                TxtSpPath.Text = dlg.FileName;
                _uiApp.Application.SharedParametersFilename = dlg.FileName;
            }
        }

        private void BtnCreateShared_Click(object sender, RoutedEventArgs e)
        {
            var wnd = new CreateSharedParameterWindow(_uiApp, _doc)
            {
                Owner = this
            };

            wnd.SharedParamFilePath = TxtSpPath.Text ?? string.Empty;

            if (wnd.ShowDialog() == true)
                Reload();
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (GridParams.SelectedItem is not ParameterRow row)
            {
                TaskDialog.Show("BA – Parameter Manager", "Select one parameter row first.");
                return;
            }

            var wnd = new EditBindingWindow(_uiApp, _doc, row) { Owner = this };
            if (wnd.ShowDialog() == true)
                Reload();
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            var selected = GridParams.SelectedItems.Cast<object>()
                .OfType<ParameterRow>()
                .ToList();

            if (selected.Count == 0)
            {
                TaskDialog.Show("BA – Parameter Manager", "Select one or more rows first.");
                return;
            }

            var td = new TaskDialog("Remove Binding")
            {
                MainInstruction = $"Remove binding for {selected.Count} parameter(s)?",
                MainContent = "This removes the project binding. It does not delete the definition from the shared parameter file.",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
            };

            if (td.Show() != TaskDialogResult.Ok)
                return;

            using var t = new Transaction(_doc, "BA – Remove Parameter Binding");
            t.Start();

            int removed = 0;
            foreach (var r in selected)
            {
                if (ParameterBindingRemover.TryRemoveBinding(_doc, r.Definition))
                    removed++;
            }

            t.Commit();
            TaskDialog.Show("BA – Parameter Manager", $"Removed bindings: {removed}");
            Reload();
        }
    }
}
