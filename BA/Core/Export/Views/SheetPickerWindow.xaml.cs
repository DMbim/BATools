using BA.Core.Export.Models;
using BA.Settings.Export;
using BA.ViewModels.Export;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Binding = System.Windows.Data.Binding;

namespace BA.Views.Export
{
    /// <summary>
    /// Modal sheet picker. Deliberately mirrors the plain code-behind
    /// pattern used by BA.UI.Sheets.DateSheetsWindow rather than the
    /// ExternalEvent-routed MVVM pattern used by ExportSettingsWindow.
    ///
    /// Unlike DateSheetsWindow, this window never touches Document,
    /// UIApplication, or any Revit API type. All sheet data is fetched
    /// through ExportUiBridge BEFORE this window is constructed and handed
    /// in as plain SheetSummary data. Dynamic parameter column discovery,
    /// value resolution, and paper size detection follow the same
    /// discipline: the caller (ExportJobEditorViewModel) supplies three
    /// delegates already bound to ExportUiBridge, this window never calls
    /// the bridge directly, same pattern NamingTemplateBuilderWindow
    /// already uses for its preview callback.
    /// </summary>
    public partial class SheetPickerWindow : Window
    {
        private readonly Action<IList<string>, Action<List<ParameterColumnCandidate>>> _requestDiscoverColumns;
        private readonly Action<IList<string>, IList<ParameterColumnDescriptor>, Action<Dictionary<string, Dictionary<string, string>>>> _requestResolveValues;
        private readonly Action<IList<string>, Action<Dictionary<string, PaperSizeInfo>>> _requestPaperSizeInfo;

        private readonly List<ParameterColumnDescriptor> _activeColumns = new List<ParameterColumnDescriptor>();

        // DataGridColumn is a DependencyObject, not a FrameworkElement, it
        // has no Tag property to carry the column key on. Tracked here
        // instead, by reference, default equality on DataGridColumn is
        // reference equality which is exactly what's needed.
        private readonly Dictionary<DataGridColumn, string> _dynamicColumnKeys = new Dictionary<DataGridColumn, string>();

        private DataGridColumn _contextMenuColumn;

        // Anchor for Shift+click range selection on the Select checkbox
        // column. Updated on every plain (non-Shift) checkbox click, left
        // untouched on Shift+click so repeated Shift+clicks extend or
        // contract from the same origin, matching Explorer-style behavior.
        // Null until the first checkbox click of the session.
        private int? _lastCheckboxClickIndex;

        public ObservableCollection<SheetPickerRowViewModel> Rows { get; } = new ObservableCollection<SheetPickerRowViewModel>();

        public SheetPickerWindow(
            IEnumerable<SheetSummary> allSheets,
            IEnumerable<string> alreadySelectedSheetNumbers,
            Action<IList<string>, Action<List<ParameterColumnCandidate>>> requestDiscoverColumns,
            Action<IList<string>, IList<ParameterColumnDescriptor>, Action<Dictionary<string, Dictionary<string, string>>>> requestResolveValues,
            Action<IList<string>, Action<Dictionary<string, PaperSizeInfo>>> requestPaperSizeInfo)
        {
            InitializeComponent();

            _requestDiscoverColumns = requestDiscoverColumns ?? throw new ArgumentNullException(nameof(requestDiscoverColumns));
            _requestResolveValues = requestResolveValues ?? throw new ArgumentNullException(nameof(requestResolveValues));
            _requestPaperSizeInfo = requestPaperSizeInfo ?? throw new ArgumentNullException(nameof(requestPaperSizeInfo));

            var alreadySelected = new HashSet<string>(
                alreadySelectedSheetNumbers ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var sheet in allSheets ?? Enumerable.Empty<SheetSummary>())
            {
                Rows.Add(new SheetPickerRowViewModel(
                    sheet.SheetNumber,
                    sheet.SheetName,
                    alreadySelected.Contains(sheet.SheetNumber)));
            }

            SheetsGrid.ItemsSource = Rows;

            var allSheetNumbers = Rows.Select(r => r.SheetNumber).ToList();

            _requestPaperSizeInfo(allSheetNumbers, infoBySheet =>
            {
                foreach (var row in Rows)
                {
                    row.PaperSizeDisplay = infoBySheet.TryGetValue(row.SheetNumber, out var info)
                        ? info.DisplayText
                        : "Unknown";
                }
            });

            // Column layout is a plain local JSON file with no Document
            // dependency, safe to load directly here rather than routing
            // through ExportUiBridge.
            var savedLayout = ParameterColumnLayoutStore.Load();

            foreach (var descriptor in savedLayout.Columns)
            {
                AddColumnOnly(descriptor);
            }

            if (_activeColumns.Count > 0)
            {
                ResolveAndPopulateColumns(_activeColumns.ToList());
            }
        }

        public List<string> GetSelectedSheetNumbers()
            => Rows.Where(r => r.IsSelected).Select(r => r.SheetNumber).ToList();

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Handles the Select checkbox column. On a plain click, records the
        /// clicked row as the new anchor. On a Shift+click, applies the
        /// clicked checkbox's resulting state to every row between the
        /// anchor and the clicked row inclusive. Range is resolved by
        /// position in the Rows collection, which is the same order the
        /// grid displays since no CollectionView sort/filter is applied to
        /// SheetsGrid.ItemsSource.
        /// </summary>
        private void SelectCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is CheckBox checkBox) || !(checkBox.DataContext is SheetPickerRowViewModel clickedRow))
            {
                return;
            }

            int clickedIndex = Rows.IndexOf(clickedRow);

            if (clickedIndex < 0)
            {
                return;
            }

            bool isShiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            if (isShiftHeld && _lastCheckboxClickIndex.HasValue)
            {
                // By the time Click fires, ToggleButton.OnClick has already
                // toggled IsChecked and the TwoWay binding has already
                // pushed that value into clickedRow.IsSelected, so this
                // reflects the final post-click state.
                bool targetState = checkBox.IsChecked == true;

                int startIndex = Math.Min(_lastCheckboxClickIndex.Value, clickedIndex);
                int endIndex = Math.Max(_lastCheckboxClickIndex.Value, clickedIndex);

                for (int i = startIndex; i <= endIndex; i++)
                {
                    Rows[i].IsSelected = targetState;
                }

                // Anchor deliberately left unchanged so a further
                // Shift+click keeps extending/contracting from the same
                // origin row.
            }
            else
            {
                _lastCheckboxClickIndex = clickedIndex;
            }
        }

        private void SheetsGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var header = FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject);
            _contextMenuColumn = header?.Column;

            if (header == null)
            {
                e.Handled = true;
            }
        }

        private void AddParameterColumn_Click(object sender, RoutedEventArgs e)
        {
            OpenAddParameterColumnDialog();
        }

        private void RemoveThisColumn_Click(object sender, RoutedEventArgs e)
        {
            if (_contextMenuColumn == null || !_dynamicColumnKeys.TryGetValue(_contextMenuColumn, out var key))
            {
                // Not a dynamic parameter column, Select/Number/Name/Paper
                // Size were never registered here, there is nothing to
                // remove.
                return;
            }

            RemoveDynamicColumn(key);
        }

        private void ResetColumns_Click(object sender, RoutedEventArgs e)
        {
            foreach (var key in _activeColumns.Select(c => c.ColumnKey).ToList())
            {
                RemoveDynamicColumn(key);
            }
        }

        private void SaveColumnLayout_Click(object sender, RoutedEventArgs e)
        {
            var layout = new ParameterColumnLayout { Columns = _activeColumns.ToList() };
            ParameterColumnLayoutStore.Save(layout);
        }

        private void OpenAddParameterColumnDialog()
        {
            var sheetNumbers = Rows.Select(r => r.SheetNumber).ToList();

            if (sheetNumbers.Count == 0)
            {
                return;
            }

            _requestDiscoverColumns(sheetNumbers, candidates =>
            {
                var alreadyAddedKeys = new HashSet<string>(_activeColumns.Select(c => c.ColumnKey), StringComparer.Ordinal);

                var picker = new ParameterColumnPickerWindow(candidates, alreadyAddedKeys) { Owner = this };

                if (picker.ShowDialog() != true)
                {
                    return;
                }

                var chosen = picker.SelectedColumns;

                if (chosen.Count == 0)
                {
                    return;
                }

                foreach (var descriptor in chosen)
                {
                    AddColumnOnly(descriptor);
                }

                ResolveAndPopulateColumns(chosen);
            });
        }

        private void AddColumnOnly(ParameterColumnDescriptor descriptor)
        {
            if (_activeColumns.Any(c => c.ColumnKey == descriptor.ColumnKey))
            {
                return;
            }

            var column = new DataGridTextColumn
            {
                Header = descriptor.DisplayName,
                Binding = new Binding($"ParameterValues[{descriptor.ColumnKey}]") { Mode = BindingMode.OneWay },
                IsReadOnly = true,
                Width = 140
            };

            SheetsGrid.Columns.Add(column);
            _dynamicColumnKeys[column] = descriptor.ColumnKey;
            _activeColumns.Add(descriptor);
        }

        private void RemoveDynamicColumn(string columnKey)
        {
            var entry = _dynamicColumnKeys.FirstOrDefault(kv => kv.Value == columnKey);

            if (entry.Key != null)
            {
                SheetsGrid.Columns.Remove(entry.Key);
                _dynamicColumnKeys.Remove(entry.Key);
            }

            _activeColumns.RemoveAll(c => c.ColumnKey == columnKey);

            foreach (var row in Rows)
            {
                row.ParameterValues.RemoveColumn(columnKey);
            }
        }

        private void ResolveAndPopulateColumns(List<ParameterColumnDescriptor> descriptors)
        {
            if (descriptors.Count == 0)
            {
                return;
            }

            var sheetNumbers = Rows.Select(r => r.SheetNumber).ToList();

            _requestResolveValues(sheetNumbers, descriptors, valuesBySheet =>
            {
                foreach (var row in Rows)
                {
                    if (!valuesBySheet.TryGetValue(row.SheetNumber, out var columnValues))
                    {
                        continue;
                    }

                    foreach (var descriptor in descriptors)
                    {
                        var key = descriptor.ColumnKey;
                        row.ParameterValues[key] = columnValues.TryGetValue(key, out var value) ? value : string.Empty;
                    }
                }
            });
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}