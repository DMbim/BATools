using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BA.Core.Export.Models;
using BA.ViewModels.Export;

namespace BA.Views.Export
{
    /// <summary>
    /// Searchable Add Parameter Column picker. Never touches Document or
    /// any Revit API type, candidates are supplied by the caller
    /// (SheetPickerWindow), which already routed the discovery request
    /// through ExportUiBridge before this window was constructed, same
    /// discipline as SheetPickerWindow and NamingTemplateBuilderWindow.
    /// </summary>
    public partial class ParameterColumnPickerWindow : Window
    {
        private readonly ObservableCollection<ParameterColumnPickerRowViewModel> _allRows =
            new ObservableCollection<ParameterColumnPickerRowViewModel>();

        public ObservableCollection<ParameterColumnPickerRowViewModel> FilteredRows { get; } =
            new ObservableCollection<ParameterColumnPickerRowViewModel>();

        public List<ParameterColumnDescriptor> SelectedColumns { get; private set; } = new List<ParameterColumnDescriptor>();

        public ParameterColumnPickerWindow(IEnumerable<ParameterColumnCandidate> candidates, IEnumerable<string> alreadyAddedColumnKeys)
        {
            InitializeComponent();

            var alreadyAdded = new HashSet<string>(alreadyAddedColumnKeys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            foreach (var candidate in candidates ?? Enumerable.Empty<ParameterColumnCandidate>())
            {
                _allRows.Add(new ParameterColumnPickerRowViewModel(candidate, alreadyAdded.Contains(candidate.ColumnKey)));
            }

            ApplyFilter(string.Empty);
            CandidatesGrid.ItemsSource = FilteredRows;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter(SearchBox.Text);
        }

        private void ApplyFilter(string filterText)
        {
            FilteredRows.Clear();

            var matches = string.IsNullOrWhiteSpace(filterText)
                ? _allRows
                : _allRows.Where(r => r.DisplayName.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0);

            foreach (var row in matches)
            {
                FilteredRows.Add(row);
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            SelectedColumns = _allRows
                .Where(r => r.IsSelected && !r.IsAlreadyAdded)
                .Select(r => (ParameterColumnDescriptor)r.Candidate)
                .ToList();

            DialogResult = true;
            Close();
        }
    }
}
