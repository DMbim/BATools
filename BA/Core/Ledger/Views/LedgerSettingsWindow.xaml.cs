using System;
using System.Windows;
using BA.BAApplication;
using BA.ViewModels;

namespace BA.UI.Ledger
{
    /// <summary>
    /// Code-behind stays thin: no Revit API calls here. All Revit-thread work goes through
    /// LedgerUiBridge from the ViewModel's RefreshCommand. The SaveFileDialog call lives here
    /// rather than in the ViewModel because it needs a real Window reference to use as its
    /// owner, and so does any MessageBox raised if that dialog fails, otherwise an error box
    /// can end up just as invisible as the original problem.
    /// </summary>
    public partial class LedgerSettingsWindow : Window
    {
        public LedgerSettingsWindow(LedgerSettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            viewModel.BrowseForFilePath = (suggestedFileName, initialDirectory) =>
            {
                AppLogger.LogInfo("LedgerSettingsWindow: BrowseForFilePath delegate invoked.");

                try
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "Ledger JSON (*.json)|*.json|All files (*.*)|*.*",
                        FileName = suggestedFileName,
                        InitialDirectory = initialDirectory,
                        OverwritePrompt = false,
                        CheckPathExists = true,
                        AddExtension = true,
                        DefaultExt = "json"
                    };

                    bool? result = dialog.ShowDialog(this);

                    AppLogger.LogInfo($"LedgerSettingsWindow: SaveFileDialog.ShowDialog returned {result}.");

                    return result == true ? dialog.FileName : null;
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("LedgerSettingsWindow: SaveFileDialog threw", ex);
                    MessageBox.Show(
                        this,
                        $"Could not open the file browser: {ex.Message}",
                        "Ledger Settings",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return null;
                }
            };
        }
    }
}