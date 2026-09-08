using BA.BAApplication.CommandRegistry;
using BA.IssueReporter.Models;
using BA.IssueReporter.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace BA.IssueReporter.Views;

public partial class SubmitIssueWindow : Window
{
    private readonly IssueReporterSettings _settings;
    private readonly string _user;
    private readonly string _projectName;
    private readonly string _projectPath;

    private BitmapSource? _screenshotImage;

    public SubmitIssueWindow(
        IssueReporterSettings settings,
        string user,
        string projectName,
        string projectPath)
    {
        InitializeComponent();

        _settings = settings;
        _user = user;
        _projectName = projectName;
        _projectPath = projectPath;

        CategoryComboBox.ItemsSource = IssueCategories.All;
        CategoryComboBox.SelectedItem = IssueCategories.Plugin;

        AutoInfoTextBlock.Text =
            $"User: {_user}\n" +
            $"Date + Time: {DateTime.Now:yyyy-MM-dd HH:mm}\n" +
            $"Project: {_projectName}";

        RefreshSourceList();
    }

    private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshSourceList();
    }

    // CHANGED: Plugin now appends IssueSources.PluginGeneric after the live command list,
    // and copies the registry's list defensively instead of mutating it in place.
    private void RefreshSourceList()
    {
        if (SourceComboBox == null || CategoryComboBox == null)
            return;

        string category = CategoryComboBox.SelectedItem?.ToString() ?? IssueCategories.Plugin;

        if (category == IssueCategories.Plugin)
        {
            var liveCommands = BACommandRegistry.GetIssueReporterCommandNames();

            // Copy defensively: GetIssueReporterCommandNames() may return a cached or shared
            // list. Appending to it directly would leak entries into it permanently across calls.
            var sources = new List<string>(liveCommands);
            sources.AddRange(IssueSources.PluginGeneric);

            SourceComboBox.ItemsSource = sources;
            SourceComboBox.SelectedIndex = sources.Count > 0 ? 0 : -1;
            return;
        }

        var categorySources = IssueSources.GetForCategory(category).ToList();

        if (categorySources.Count == 0)
        {
            categorySources.Add("Other");
        }

        SourceComboBox.ItemsSource = categorySources;
        SourceComboBox.SelectedIndex = 0;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control)
            return;

        TryAttachClipboardImage(e);
    }

    private void TryAttachClipboardImage(KeyEventArgs e)
    {
        bool hasImage;

        try
        {
            hasImage = Clipboard.ContainsImage();
        }
        catch (Exception)
        {
            return;
        }

        if (!hasImage)
            return;

        BitmapSource? clipboardImage;

        try
        {
            clipboardImage = Clipboard.GetImage();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not read the image from the clipboard.\n\n{ex.Message}",
                "Clipboard Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            e.Handled = true;
            return;
        }

        if (clipboardImage == null)
            return;

        if (clipboardImage.CanFreeze)
        {
            clipboardImage.Freeze();
        }

        _screenshotImage = clipboardImage;

        ScreenshotPreviewImage.Source = _screenshotImage;
        ScreenshotPreviewBorder.Visibility = System.Windows.Visibility.Visible;
        RemoveScreenshotButton.Visibility = System.Windows.Visibility.Visible;

        e.Handled = true;
    }

    private void RemoveScreenshotButton_Click(object sender, RoutedEventArgs e)
    {
        _screenshotImage = null;
        ScreenshotPreviewImage.Source = null;
        ScreenshotPreviewBorder.Visibility = System.Windows.Visibility.Collapsed;
        RemoveScreenshotButton.Visibility = System.Windows.Visibility.Collapsed;
    }

    private string SaveScreenshotToDisk(string issueId)
    {
        if (_screenshotImage == null)
            return string.Empty;

        string? databaseFolder = Path.GetDirectoryName(_settings.IssueDatabasePath);

        if (string.IsNullOrWhiteSpace(databaseFolder))
        {
            throw new InvalidOperationException(
                "Could not determine the attachments folder from IssueDatabasePath.");
        }

        string attachmentsFolder = Path.Combine(databaseFolder, "Attachments");
        Directory.CreateDirectory(attachmentsFolder);

        string filePath = Path.Combine(attachmentsFolder, $"{issueId}.png");

        BitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(_screenshotImage));

        using (FileStream stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        {
            encoder.Save(stream);
        }

        return filePath;
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        string category = CategoryComboBox.SelectedItem?.ToString() ?? IssueCategories.Other;
        string source = SourceComboBox.Text?.Trim() ?? string.Empty;
        string issueText = IssueTextBox.Text?.Trim() ?? string.Empty;
        string suggestion = SuggestionTextBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(category))
        {
            MessageBox.Show(
                "Please select a category.",
                "Missing Category",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            MessageBox.Show(
                "Please enter or select a source.",
                "Missing Source",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (string.IsNullOrWhiteSpace(issueText))
        {
            MessageBox.Show(
                "Please describe the issue.",
                "Missing Issue",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var issue = new PluginIssue
        {
            Category = category,
            Source = source,
            Issue = issueText,
            Suggestion = suggestion,
            User = _user,
            SubmittedAt = DateTime.Now,
            ProjectName = _projectName,
            ProjectPath = _projectPath,
            Status = IssueStatuses.New
        };

        if (_screenshotImage != null)
        {
            try
            {
                issue.ImagePath = SaveScreenshotToDisk(issue.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"The issue text will still be submitted, but the screenshot could not be saved.\n\n{ex.Message}",
                    "Screenshot Save Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                issue.ImagePath = string.Empty;
            }
        }

        try
        {
            var storage = new IssueStorageService(_settings.IssueDatabasePath);
            storage.AddIssue(issue);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Issue could not be saved.\n\n" +
                $"Message:\n{ex.Message}\n\n" +
                $"Source:\n{ex.Source}\n\n" +
                $"StackTrace:\n{ex.StackTrace}",
                "BA Issue Reporter Storage Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        try
        {
            var notifier = new NotificationService(_settings.TeamsWorkflowUrl);
            await notifier.NotifyIssueSubmittedAsync(issue);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Issue was saved, but Teams notification failed.\n\n" +
                $"Message:\n{ex.Message}\n\n" +
                $"Source:\n{ex.Source}",
                "BA Issue Reporter Notification Warning",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        MessageBox.Show(
            $"Issue submitted successfully.\n\n{issue.DisplayNumber}",
            "Submitted",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}