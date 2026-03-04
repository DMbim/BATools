using Autodesk.Revit.UI;
using BA.UI.TextHub.ExternalEvents;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BA.UI.TextHub
{
    public partial class TextHubWindow : Window, INotifyPropertyChanged
    {
        private static TextHubWindow? _instance;

        private readonly UIApplication _uiApp;
        private readonly TextHubExternalInvoker _revit;

        private List<TextStyleRow> _allRows = new();
        private string _statusText = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText == value) return;
                _statusText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            }
        }

        public static void ShowOrFocus(UIApplication uiApp)
        {
            if (_instance == null || !_instance.IsLoaded)
            {
                _instance = new TextHubWindow(uiApp);

                var revitHandle = uiApp.MainWindowHandle;
                new System.Windows.Interop.WindowInteropHelper(_instance).Owner = revitHandle;

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

        private TextHubWindow(UIApplication uiApp)
        {
            InitializeComponent();
            DataContext = this;
            // Hard UI sanity defaults (prevents huge rows + improves perf)
            GridTypes.RowHeight = 30;
            GridTypes.EnableRowVirtualization = true;
            GridTypes.EnableColumnVirtualization = true;
            VirtualizingPanel.SetIsVirtualizing(GridTypes, true);
            VirtualizingPanel.SetVirtualizationMode(GridTypes, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(GridTypes, true);

            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _revit = new TextHubExternalInvoker(_uiApp);

            Loaded += (_, __) => Reload();
            Closing += (_, __) => { /* keep instance logic simple */ };
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _revit = new TextHubExternalInvoker(_uiApp);

            Loaded += (_, __) => Reload();
            Closing += (_, __) => { /* keep instance logic simple */ };
        }



        private void Reload()
        {
            try
            {
                var doc = _uiApp.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    StatusText = "No active document.";
                    GridTypes.ItemsSource = null;

                    return;
                }

                _allRows = TextHubCollector.Collect(doc)
                                          .OrderBy(r => r.Kind)
                                          .ThenBy(r => r.FamilyName)
                                          .ThenBy(r => r.TypeName)
                                          .ToList();

                ApplyFiltersToGrid();
                StatusText = $"Loaded {_allRows.Count} types. Editable: {_allRows.Count(x => x.IsEditable)}";
            }
            catch (Exception ex)
            {
                StatusText = "Reload failed: " + ex.Message;
            }
        }

        private void ApplyFiltersToGrid()
        {
            string s = (TxtSearch.Text ?? "").Trim();
            bool onlyEditable = ChkShowOnlyEditable.IsChecked == true;

            IEnumerable<TextStyleRow> q = _allRows;

            if (onlyEditable)
                q = q.Where(x => x.IsEditable);

            if (!string.IsNullOrWhiteSpace(s))
            {
                var ss = s.ToLowerInvariant();
                q = q.Where(x =>
                    (x.Kind ?? "").ToLowerInvariant().Contains(ss) ||
                    (x.FamilyName ?? "").ToLowerInvariant().Contains(ss) ||
                    (x.TypeName ?? "").ToLowerInvariant().Contains(ss) ||
                    (x.TextFont ?? "").ToLowerInvariant().Contains(ss));
            }

            var list = q.ToList();
            GridTypes.ItemsSource = list;

            // DEBUG: count truly empty-looking rows
            int emptyish = list.Count(r =>
                string.IsNullOrWhiteSpace(r.Kind) &&
                string.IsNullOrWhiteSpace(r.FamilyName) &&
                string.IsNullOrWhiteSpace(r.TypeName) &&
                string.IsNullOrWhiteSpace(r.TextFont) &&
                (r.TextSizeMm == null));

            StatusText = $"Loaded {_allRows.Count} types. Shown: {list.Count}. Editable: {_allRows.Count(x => x.IsEditable)}. EmptyRows: {emptyish}";
        }

        private static bool ContainsInvariant(string? haystack, string needleLower)
        {
            if (string.IsNullOrEmpty(haystack)) return false;
            return haystack.IndexOf(needleLower, StringComparison.InvariantCultureIgnoreCase) >= 0;
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e) => Reload();

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var rows = _allRows.Where(r => r.IsDirty && r.IsEditable).ToList();
                if (rows.Count == 0)
                {
                    StatusText = "No changes to apply.";
                    return;
                }

                _revit.ApplyTextStyleEdits(rows, result =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusText = result;
                        // after apply, refresh values from doc for accuracy
                        Reload();
                    });
                });

                StatusText = $"Applying changes ({rows.Count})…";
            }
            catch (Exception ex)
            {
                StatusText = "Apply failed: " + ex.Message;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFiltersToGrid();

        private void ChkShowOnlyEditable_Changed(object sender, RoutedEventArgs e) => ApplyFiltersToGrid();
    }
}