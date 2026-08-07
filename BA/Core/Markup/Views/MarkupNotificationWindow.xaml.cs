// BA/Markup/Views/MarkupNotificationWindow.xaml.cs
using System.Windows;
using BA.Markup.ViewModels;

namespace BA.Markup.Views
{
    public partial class MarkupNotificationWindow : Window
    {
        public MarkupNotificationWindow(MarkupNotificationViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.CloseRequested += (_, _) => Close();
        }
    }
}