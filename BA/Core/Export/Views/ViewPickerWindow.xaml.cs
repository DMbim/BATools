using BA.Core.Export.Models;
using BA.ViewModels.Export;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace BA.Views.Export
{
    /// <summary>
    /// Modal view picker for Views mode export jobs. Simpler than
    /// SheetPickerWindow deliberately, no dynamic parameter columns or
    /// paper size, arbitrary views don't have an equivalent to a sheet's
    /// title block, matching the simpler picker style already used for
    /// Family Export instead. Never touches Document, all view data is
    /// fetched through ExportUiBridge before this window is constructed.
    /// </summary>
    public partial class ViewPickerWindow : Window
    {
        public ObservableCollection<ViewPickerRowViewModel> FilteredRows { get; } = new ObservableCollection<ViewPickerRowViewModel>();

        private readonly List<ViewPickerRowViewModel> _allRows = new List<ViewPickerRowViewModel>();

        public ViewPickerWindow(IEnumerable<ViewSummary> allViews, IEnumerable<string> alreadySelectedUniqueIds)
        {
            InitializeComponent();

            var alreadySelected = new HashSet<string>(
                alreadySelectedUniqueIds ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);

            foreach (var view in allViews ?? Enumerable.Empty<ViewSummary>())
            {
                _allRows.Add(new ViewPickerRowViewModel(view, alreadySelected.Contains(view.UniqueId)));
            }

            ApplyFilter(string.Empty);
            ViewsGrid.ItemsSource = FilteredRows;
        }

        public List<string> GetSelectedViewUniqueIds()
            => _allRows.Where(r => r.IsSelected).Select(r => r.Info.UniqueId).ToList();

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ApplyFilter(SearchBox.Text);
        }

        private void ApplyFilter(string filterText)
        {
            FilteredRows.Clear();

            var matches = string.IsNullOrWhiteSpace(filterText)
                ? _allRows
                : _allRows.Where(r =>
                    r.Name.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    r.ViewType.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0);

            foreach (var row in matches)
            {
                FilteredRows.Add(row);
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in FilteredRows)
            {
                row.IsSelected = true;
            }
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in FilteredRows)
            {
                row.IsSelected = false;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}