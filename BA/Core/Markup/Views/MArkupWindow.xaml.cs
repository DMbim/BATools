// BA/Markup/Views/MarkupWindow.xaml.cs
using System.Windows;
using BA.Markup.ViewModels;

namespace BA.Markup.Views
{
    public partial class MarkupWindow : Window
    {
        public MarkupWindow(MarkupViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.CloseRequested += (_, _) => Close();
        }
    }
}