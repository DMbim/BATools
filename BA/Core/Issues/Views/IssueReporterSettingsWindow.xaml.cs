using BA.IssueReporter.Models;
using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace BA.IssueReporter.Views;

public partial class IssueReporterSettingsWindow : Window
{
    public IssueReporterSettings Settings { get; private set; }

    public IssueReporterSettingsWindow(IssueReporterSettings settings, string settingsPath)
    {
        InitializeComponent();

        Settings = settings;
        SettingsPathTextBlock.Text = $"Settings file: {settingsPath}";
        IssueDatabasePathTextBox.Text = settings.IssueDatabasePath;
        TeamsWorkflowUrlPasswordBox.Password = settings.TeamsWorkflowUrl;
        ManagersTextBox.Text = string.Join(Environment.NewLine, settings.ManagerUsers);

    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string databasePath = IssueDatabasePathTextBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(databasePath))
        {
            MessageBox.Show("Issue database path is required.", "Missing Database Path", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!databasePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Issue database path must point to a .json file.", "Invalid Database Path", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? folder = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
        {
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not create database folder.\n\n{ex.Message}", "Folder Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        Settings.IssueDatabasePath = databasePath;
        Settings.TeamsWorkflowUrl = TeamsWorkflowUrlPasswordBox.Password?.Trim() ?? string.Empty;
        Settings.ManagerUsers = ManagersTextBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();


        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
