using BA.ViewModels;

namespace BA.Views
{
    public sealed partial class BAView
    {
        public BAView(BAViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }
    }
}