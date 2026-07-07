using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BA.UI.LineStyleHub
{
    public partial class LineStyleHubWindow : Window, INotifyPropertyChanged
    {
        private static LineStyleHubWindow? _instance;

        private readonly UIApplication _uiApp;
        private readonly LineStyleExternalInvoker _invoker;

        private List<LineStyleRow> _allRows = new();
        private List<PatternEntry> _patterns = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        // Bound to the pattern ComboBox via DataContext.PatternNames
        private List<string> _patternNames = new();
        public List<string> PatternNames
        {
            get => _patternNames;
            private set
            {
                _patternNames = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternNames)));
            }
        }

        // ── Singleton management ─────────────────────────────────────────────
        public static LineStyleHubWindow GetOrCreate(UIApplication uiApp, LineStyleExternalInvoker invoker)
        {
            if (_instance == null || !_instance.IsLoaded)
                _instance = new LineStyleHubWindow(uiApp, invoker);
            return _instance;
        }

        public static void ShowOrFocus(UIApplication uiApp, LineStyleExternalInvoker invoker)
        {
            if (_instance == null || !_instance.IsLoaded)
            {
                _instance = new LineStyleHubWindow(uiApp, invoker);
                var handle = uiApp.MainWindowHandle;
                new System.Windows.Interop.WindowInteropHelper(_instance).Owner = handle;
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

        // ── Constructor ──────────────────────────────────────────────────────
        private LineStyleHubWindow(UIApplication uiApp, LineStyleExternalInvoker invoker)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

            InitializeComponent();
            DataContext = this;

            GridStyles.EnableRowVirtualization = true;
            GridStyles.EnableColumnVirtualization = true;
            VirtualizingPanel.SetIsVirtualizing(GridStyles, true);
            VirtualizingPanel.SetVirtualizationMode(GridStyles, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(GridStyles, true);

            Loaded += (_, __) => Reload();
        }

        // ── Data loading ─────────────────────────────────────────────────────
        private void Reload()
        {
            try
            {
                var doc = _uiApp.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    SetStatus("No active document.");
                    GridStyles.ItemsSource = null;
                    return;
                }

                var (rows, patterns) = LineStyleCollector.Collect(doc);
                _allRows = rows;
                _patterns = patterns;
                PatternNames = patterns.Select(p => p.Name).ToList();

                ApplyFilters();
                SetStatus($"Loaded {_allRows.Count} styles.");
            }
            catch (Exception ex)
            {
                SetStatus("Reload failed: " + ex.Message);
            }
        }

        private void ApplyFilters()
        {
            var search = (TxtSearch.Text ?? "").Trim().ToLowerInvariant();
            bool onlyDirty = ChkOnlyDirty.IsChecked == true;

            IEnumerable<LineStyleRow> q = _allRows;

            if (onlyDirty)
                q = q.Where(r => r.IsDirty);

            if (!string.IsNullOrWhiteSpace(search))
            {
                q = q.Where(r =>
                    (r.CategoryName ?? "").ToLowerInvariant().Contains(search) ||
                    (r.ParentCategoryName ?? "").ToLowerInvariant().Contains(search) ||
                    (r.PatternName ?? "").ToLowerInvariant().Contains(search));
            }

            var list = q.ToList();
            GridStyles.ItemsSource = list;

            int dirty = _allRows.Count(r => r.IsDirty);
            int markedDelete = _allRows.Count(r => r.IsMarkedForDelete);
            SetStatus($"Loaded {_allRows.Count} styles. Shown: {list.Count}. Changed: {dirty}. Marked for delete: {markedDelete}.");
        }

        // ── Color swatch click: open WPF color picker ────────────────────────
        private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is LineStyleRow row)
            {
                if (!row.IsEditable) return;

                var dialog = new ColorPickerDialog(row.Color)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true)
                {
                    row.Color = dialog.SelectedColor;
                    // Force grid to refresh the swatch — ItemsSource binding does not refresh
                    // automatically for complex cell templates. Reassign to refresh.
                    var source = GridStyles.ItemsSource;
                    GridStyles.ItemsSource = null;
                    GridStyles.ItemsSource = source;
                }
            }
        }

        // ── Apply ────────────────────────────────────────────────────────────
        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var rows = _allRows.Where(r => r.IsDirty).ToList();
                if (rows.Count == 0)
                {
                    SetStatus("No changes to apply.");
                    return;
                }

                SetStatus($"Applying {rows.Count} change(s)...");

                _invoker.ApplyEdits(rows, _patterns, (result, errors) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (errors.Count > 0)
                        {
                            // Show full error list in a dialog for blocked deletes or multi-errors
                            MessageBox.Show(
                                result + "\n\n" + string.Join("\n", errors),
                                "BA · Line Style Manager",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                        else
                        {
                            SetStatus(result);
                        }
                        Reload();
                    });
                });
            }
            catch (Exception ex)
            {
                SetStatus("Apply failed: " + ex.Message);
            }
        }

        // ── Reset ────────────────────────────────────────────────────────────
        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _allRows)
                r.ResetDirty();
            ApplyFilters();
            SetStatus("All changes discarded.");
        }

        // ── Other buttons ────────────────────────────────────────────────────
        private void BtnReload_Click(object sender, RoutedEventArgs e) => Reload();
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();
        private void ChkFilter_Changed(object sender, RoutedEventArgs e) => ApplyFilters();

        private void SetStatus(string text) => TxtStatus.Text = text;
    }
}
