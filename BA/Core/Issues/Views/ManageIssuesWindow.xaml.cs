using BA.IssueReporter.Models;
using BA.IssueReporter.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

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
            UpdateScreenshotPreview();
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

        UpdateScreenshotPreview(); // NEW
    }

    // NEW: loads the attached screenshot, if any, for the currently selected issue.
    private void UpdateScreenshotPreview()
    {
        string? imagePath = _selectedIssue?.ImagePath;

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            ScreenshotThumbnailImage.Source = null;
            ScreenshotThumbnailBorder.Visibility = System.Windows.Visibility.Collapsed;
            OpenScreenshotButton.Visibility = System.Windows.Visibility.Collapsed;
            NoScreenshotTextBlock.Visibility = System.Windows.Visibility.Visible;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // loads and releases the file handle immediately
            bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            ScreenshotThumbnailImage.Source = bitmap;
            ScreenshotThumbnailBorder.Visibility = System.Windows.Visibility.Visible;
            OpenScreenshotButton.Visibility = System.Windows.Visibility.Visible;
            NoScreenshotTextBlock.Visibility = System.Windows.Visibility.Collapsed;
        }
        catch (Exception)
        {
            // Corrupt or unreadable image file, fall back to the "no screenshot" state
            // rather than throwing out of a SelectionChanged handler.
            ScreenshotThumbnailImage.Source = null;
            ScreenshotThumbnailBorder.Visibility = System.Windows.Visibility.Collapsed;
            OpenScreenshotButton.Visibility = System.Windows.Visibility.Collapsed;
            NoScreenshotTextBlock.Visibility = System.Windows.Visibility.Visible;
        }
    }

    // NEW
    private void OpenScreenshotButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIssue == null
            || string.IsNullOrWhiteSpace(_selectedIssue.ImagePath)
            || !File.Exists(_selectedIssue.ImagePath))
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo(_selectedIssue.ImagePath)
            {
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open the screenshot file.\n\n{ex.Message}",
                "Open Screenshot Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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
                exportFolder = @"S:\CAD\Autodesk Revit\_admin\BA_tools\BA_Issues\CSV";
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