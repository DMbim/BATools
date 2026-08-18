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
        private readonly TextHubExternalInvoker _invoker;

        private List<TextStyleRow> _allRows = new();
        private string _statusText = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        public static TextHubWindow GetOrCreate(UIApplication uiApp, TextHubExternalInvoker invoker)
        {
            if (_instance == null || !_instance.IsLoaded)
            {
                _instance = new TextHubWindow(uiApp, invoker);
            }
            return _instance;
        }

        public static void ShowOrFocus(UIApplication uiApp, TextHubExternalInvoker invoker)
        {
            if (_instance == null || !_instance.IsLoaded)
            {
                _instance = new TextHubWindow(uiApp, invoker);

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

        private TextHubWindow(UIApplication uiApp, TextHubExternalInvoker invoker)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));       // <- KEPT ONCE
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker)); // <- KEPT ONCE, no re-wrap

            InitializeComponent();
            DataContext = this;

            GridTypes.RowHeight = 30;
            GridTypes.EnableRowVirtualization = true;
            GridTypes.EnableColumnVirtualization = true;
            VirtualizingPanel.SetIsVirtualizing(GridTypes, true);
            VirtualizingPanel.SetVirtualizationMode(GridTypes, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(GridTypes, true);

            Loaded += (_, __) => Reload();   // <- REGISTERED ONCE
            Closing += (_, __) => { };       // <- REGISTERED ONCE
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

            int emptyish = list.Count(r =>
                string.IsNullOrWhiteSpace(r.Kind) &&
                string.IsNullOrWhiteSpace(r.FamilyName) &&
                string.IsNullOrWhiteSpace(r.TypeName) &&
                string.IsNullOrWhiteSpace(r.TextFont) &&
                (r.TextSizeMm == null));

            StatusText = $"Loaded {_allRows.Count} types. Shown: {list.Count}. Editable: {_allRows.Count(x => x.IsEditable)}. EmptyRows: {emptyish}";
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

                _invoker.ApplyTextStyleEdits(rows, result =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusText = result;
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

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFiltersToGrid();

        private void ChkShowOnlyEditable_Changed(object sender, RoutedEventArgs e) => ApplyFiltersToGrid();
    }
}