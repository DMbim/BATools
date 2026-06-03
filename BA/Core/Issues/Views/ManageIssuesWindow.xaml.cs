using BA.IssueReporter.Models;
using BA.IssueReporter.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace BA.IssueReporter.Views;

public partial class ManageIssuesWindow : Window
{
    private readonly IssueReporterSettings _settings;
    private readonly string _managerUser;
    private List<PluginIssue> _issues = new();
    private PluginIssue? _selectedIssue;

    public ManageIssuesWindow(IssueReporterSettings settings, string managerUser)
    {
        InitializeComponent();
        _settings = settings;
        _managerUser = managerUser;
        StatusComboBox.ItemsSource = IssueStatuses.All;
        LoadIssues();
    }

    private void LoadIssues()
    {
        var storage = new IssueStorageService(_settings.IssueDatabasePath);
        _issues = storage.LoadIssues().OrderByDescending(x => x.SubmittedAt).ToList();
        IssuesDataGrid.ItemsSource = null;
        IssuesDataGrid.ItemsSource = _issues;
    }

    private void IssuesDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selectedIssue = IssuesDataGrid.SelectedItem as PluginIssue;

        if (_selectedIssue == null)
        {
            SelectedIssueInfoTextBlock.Text = "No issue selected";
            IssueDetailsTextBox.Text = string.Empty;
            ManagerCommentTextBox.Text = string.Empty;
            StatusComboBox.SelectedItem = null;
            return;
        }

        SelectedIssueInfoTextBlock.Text =
            $"Number: {_selectedIssue.DisplayNumber}\n" +
            $"Category: {_selectedIssue.Category}\n" +
            $"Source: {_selectedIssue.Source}\n" +
            $"User: {_selectedIssue.User}\n" +
            $"Project: {_selectedIssue.ProjectName}\n" +
            $"Submitted: {_selectedIssue.SubmittedAt:yyyy-MM-dd HH:mm}";

        IssueDetailsTextBox.Text =
            $"ISSUE:\n{_selectedIssue.Issue}\n\n" +
            $"SUGGESTION:\n{(string.IsNullOrWhiteSpace(_selectedIssue.Suggestion) ? "—" : _selectedIssue.Suggestion)}";

        ManagerCommentTextBox.Text = _selectedIssue.ManagerComment ?? string.Empty;
        StatusComboBox.SelectedItem = _selectedIssue.Status;
    }

    private async void SaveUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIssue == null)
        {
            MessageBox.Show("Please select an issue first.", "No Issue Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (StatusComboBox.SelectedItem == null)
        {
            MessageBox.Show("Please select a status.", "Missing Status", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _selectedIssue.Status = StatusComboBox.SelectedItem.ToString() ?? IssueStatuses.New;
        _selectedIssue.ManagerComment = ManagerCommentTextBox.Text?.Trim() ?? string.Empty;
        _selectedIssue.LastUpdatedBy = _managerUser;
        _selectedIssue.LastUpdatedAt = DateTime.Now;

        try
        {
            var storage = new IssueStorageService(_settings.IssueDatabasePath);
            storage.UpdateIssue(_selectedIssue);

            var notifier = new NotificationService(_settings.TeamsWorkflowUrl);
            await notifier.NotifyIssueUpdatedAsync(_selectedIssue);

            MessageBox.Show("Issue updated successfully.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadIssues();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Issue could not be updated.\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string exportFolder = _settings.CsvExportFolderPath;

            if (string.IsNullOrWhiteSpace(exportFolder))
            {
                exportFolder = @"S:\CAD\Autodesk Revit\BA_Resources\BA_Issues\CSV";
            }

            Directory.CreateDirectory(exportFolder);

            string filePath = Path.Combine(
                exportFolder,
                $"BA_Issues_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            IssueExportService.ExportToCsv(_issues, filePath);

            MessageBox.Show(
                $"Issues exported successfully:\n\n{filePath}",
                "Export Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not export issues.\n\n{ex.Message}",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadIssues();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

}
