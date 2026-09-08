// File: BA_Tools/CadPurge/Views/CadPurgeWindow.xaml.cs
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BA.CadPurge.ViewModels;

namespace BA.CadPurge.Views
{
    public partial class CadPurgeWindow : Window
    {
        /// <summary>
        /// The "sticky group" for each grid. WPF.DataGrid.SelectedItems is not a bindable
        /// dependency property, so this is the bridge that forwards the raw row selection into
        /// CadPurgeViewModel. The two grids are tracked independently since Line Patterns and
        /// Text Styles are separate ItemsSource collections; only one grid is interactable at a
        /// time anyway since they live in separate tabs.
        /// </summary>
        private readonly List<PurgeCandidateViewModel> _linePatternStickyGroup = new();
        private readonly List<PurgeCandidateViewModel> _textStyleStickyGroup = new();

        public CadPurgeWindow()
        {
            InitializeComponent();
            DataContext = new CadPurgeViewModel();
        }

        private void LinePatternGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStickyGroup((DataGrid)sender, _linePatternStickyGroup);
        }

        private void TextStyleGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStickyGroup((DataGrid)sender, _textStyleStickyGroup);
        }

        /// <summary>
        /// WPF.DataGrid often collapses a multi row highlight down to a single row the moment you
        /// click into an interactive control (the Action combo box) inside one of those rows,
        /// because DataGridCell intercepts the mouse down for selection before the click reaches
        /// the child control, even when that row was already part of the highlight. Without
        /// accounting for that, opening the combo box on one of several highlighted rows would
        /// narrow grid.SelectedItems down to that single row before the new action value is ever
        /// read by the ViewModel.
        ///
        /// The group is only replaced when a genuine multi row selection gesture happens
        /// (Count greater than 1, via Ctrl or Shift click, drag select, or keyboard). A later
        /// single click that lands on a row already inside the current group does not shrink it,
        /// since that is interpreted as "still working within the group" rather than a fresh
        /// single row pick. A single click on a row outside the current group is a normal new
        /// selection and replaces the group with just that row, which is the correct behavior for
        /// an ordinary one row edit.
        /// </summary>
        private void UpdateStickyGroup(DataGrid grid, List<PurgeCandidateViewModel> stickyGroup)
        {
            List<PurgeCandidateViewModel> current = grid.SelectedItems
                .Cast<PurgeCandidateViewModel>()
                .ToList();

            if (current.Count > 1)
            {
                stickyGroup.Clear();
                stickyGroup.AddRange(current);
            }
            else if (current.Count == 1)
            {
                if (!stickyGroup.Contains(current[0]))
                {
                    stickyGroup.Clear();
                    stickyGroup.Add(current[0]);
                }
                // Else the click landed back on an existing group member, keep the group intact.
            }
            else
            {
                stickyGroup.Clear();
            }

            if (DataContext is CadPurgeViewModel viewModel)
                viewModel.SetHighlightedGroup(stickyGroup);
        }
    }
}