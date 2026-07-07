using BATools.SelectionManager.ViewModels;
using System.Windows;

namespace BATools.SelectionManager.Views
{
    public partial class ToolbarSettingsWindow : Window
    {
        public ToolbarSettingsWindow(ToolbarSettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // ViewModel signals close via event — keeps VM free of Window reference
            viewModel.CloseRequested += result =>
            {
                DialogResult = result;
            };
        }
    }
}