// BA/Markup/Views/MarkupWindow.xaml.cs
using System.Windows;
using BA.Markup.ViewModels;

namespace BA.Markup.Views
{

    public partial class MarkupWindow : Window
    {
        private MarkupViewModel? _viewModel;

        public MarkupWindow(MarkupViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.CloseRequested += (_, _) => Close();
            // TEMP DIAGNOSTIC — remove after confirming
            System.Diagnostics.Debug.WriteLine(
                $"[MarkupWindow] TypeOptions.Count = {viewModel.TypeOptions.Count}");
            System.Diagnostics.Debug.WriteLine(
                $"[MarkupWindow] DataContext type = {DataContext?.GetType().Name ?? "NULL"}");
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // TEMP DIAGNOSTIC — remove after confirming
            System.Diagnostics.Debug.WriteLine("[MarkupWindow] Loaded event fired.");
        }
    }
}