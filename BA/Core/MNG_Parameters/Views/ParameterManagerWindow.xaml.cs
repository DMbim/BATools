using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Parameters;
using BA.UI.ExternalEvents;
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
        private readonly RevitExternalInvoker _revit;
        
        private readonly ObservableCollection<ParameterRow> _rows = new();
        private string _search = "";

        public ParameterManagerWindow(UIApplication uiApp, Document doc, RevitExternalInvoker revit)
        {
            InitializeComponent();

            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _revit = revit ?? throw new ArgumentNullException(nameof(revit));

            GridParams.ItemsSource = _rows;

            TxtSpPath.Text = _uiApp.Application.SharedParametersFilename ?? "";
            ReloadViaRevit();
        }

        private static Document ResolveDoc(UIApplication app, Document preferred)
        {
            if (preferred != null && preferred.IsValidObject) return preferred;
            return app.ActiveUIDocument?.Document;
        }

        private void ReloadViaRevit()
        {
            _revit.Run(app =>
            {
                var d = ResolveDoc(app, _doc);
                if (d == null) return Array.Empty<ParameterRow>();

                return ParameterBindingCollector.Collect(d)
                    .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            },
            onCompleted: result =>
            {
                _rows.Clear();
                foreach (var r in result) _rows.Add(r);

                ApplySearch();
                TxtSpPath.Text = _uiApp.Application.SharedParametersFilename ?? "";
            },
            onError: ex => TaskDialog.Show("BA – Parameter Manager", ex.ToString()));
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

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => ReloadViaRevit();

        private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _search = TxtSearch.Text ?? "";
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

            if (dlg.ShowDialog() != true) return;

            var path = dlg.FileName;
            TxtSpPath.Text = path;

            _revit.Run(app => app.Application.SharedParametersFilename = path,
                onCompleted: ReloadViaRevit,
                onError: ex => TaskDialog.Show("BA – Parameter Manager", ex.ToString()));
        }

        private void BtnAddShared_Click(object sender, RoutedEventArgs e)
        {
            var win = new CreateSharedParameterWindow(_uiApp, _doc, _revit)
            {
                Owner = this
            };

            // Refresh ParameterManager after binding/injection finishes
            win.Applied += () => ReloadViaRevit();

            // Optional: prevent user from interacting with ParameterManager while Create window is open
            IsEnabled = false;
            win.Closed += (_, __) => IsEnabled = true;

            win.Show(); // modeless -> ExternalEvent runs reliably
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            var selected = GridParams.SelectedItems.Cast<object>()
                .OfType<ParameterRow>()
                .ToList();

            if (selected.Count == 0)
            {
                TaskDialog.Show("BA – Parameter Manager", "Select one or more parameters first.");
                return;
            }

            var win = new EditBindingWindow(_uiApp, _doc, selected, _revit)
            {
                Owner = this
            };

            if (win.ShowDialog() == true)
                ReloadViaRevit();
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

            _revit.Run(app =>
            {
                var d = ResolveDoc(app, _doc);
                if (d == null) throw new InvalidOperationException("No active document.");

                using var t = new Transaction(d, "BA – Remove Parameter Binding");
                t.Start();

                int removed = 0;
                foreach (var r in selected)
                {
                    if (ParameterBindingRemover.TryRemoveBinding(d, r.Name, r.Guid))
                        removed++;
                }

                t.Commit();

                TaskDialog.Show("BA – Parameter Manager", $"Removed bindings: {removed}");
            },
            onCompleted: ReloadViaRevit,
            onError: ex => TaskDialog.Show("BA – Parameter Manager", ex.ToString()));
        }

        private void GridParams_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var selected = GridParams.SelectedItems.Cast<object>().OfType<ParameterRow>().ToList();
            if (selected.Count == 0)
            {
                TxtSelectionSummary.Text = "";
                return;
            }

            if (selected.Count == 1)
            {
                var r = selected[0];
                TxtSelectionSummary.Text = $"Selected: 1 | {r.InstanceOrType} | {r.GroupLabel} | Cats: {r.CategoryIdValues?.Count ?? 0}";
                return;
            }

            // Multi selection summary
            var union = new HashSet<long>();
            foreach (var r in selected)
            {
                if (r.CategoryIdValues == null) continue;
                foreach (var id in r.CategoryIdValues) union.Add(id);
            }

            TxtSelectionSummary.Text = $"Selected: {selected.Count} | Any Cats: {union.Count}";
        }
    }
}
