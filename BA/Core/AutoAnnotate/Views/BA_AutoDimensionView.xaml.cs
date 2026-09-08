using System.Windows;
using System.Windows.Controls;
using BA.BIM.Commands.Dimension;
using BA.BIM.Core.Dimensioning.Models;

namespace BA.AutoAnnotate.Views
{
    public partial class BA_AutoDimensionView : Window
    {
        public BA_AutoDimensionView(BA_AutoDimensionViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private async void BtnDeleteOutcome_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is BA_DimensionPlacementOutcome outcome
                && DataContext is BA_AutoDimensionViewModel vm)
            {
                await vm.DeleteOutcomeAsync(outcome);
            }
        }

        private async void CandidatesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is BA_AutoDimensionViewModel vm
                && sender is DataGrid grid
                && grid.SelectedItem is BA_DimensionCandidateVM vmItem)
            {
                await vm.HighlightCandidateAsync(vmItem.Model);
            }
        }

        private async void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is BA_AutoDimensionViewModel vm
                && sender is DataGrid grid
                && grid.SelectedItem is BA_DimensionPlacementOutcome outcome)
            {
                await vm.HighlightOutcomeAsync(outcome);
            }
        }
    }
}