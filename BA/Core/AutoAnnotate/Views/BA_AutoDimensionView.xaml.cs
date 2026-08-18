using System.Windows;
using BA.BIM.Commands.Dimension;
namespace BA.AutoAnnotate.Views
{
    public partial class BA_AutoDimensionView : Window
    {
        public BA_AutoDimensionView(BA_AutoDimensionViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}