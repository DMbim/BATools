using BA.BAApplication.CommandRegistry;
using BA.IssueReporter.Models;
using BA.IssueReporter.Services;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BA.IssueReporter.Views;

public partial class SubmitIssueWindow : Window
{
    private readonly IssueReporterSettings _settings;
    private readonly string _user;
    private readonly string _projectName;
    private readonly string _projectPath;

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

    private void RefreshSourceList()
    {
        if (SourceComboBox == null || CategoryComboBox == null)
            return;

        string category = CategoryComboBox.SelectedItem?.ToString() ?? IssueCategories.Plugin;

        if (category == IssueCategories.Plugin)
        {
            var commands = BACommandRegistry.GetIssueReporterCommandNames();

            if (commands.Count == 0)
            {
                commands.Add("Other Plugin Issue");
            }

            SourceComboBox.ItemsSource = commands;
            SourceComboBox.SelectedIndex = commands.Count > 0 ? 0 : -1;
            return;
        }

        var sources = IssueSources.GetForCategory(category).ToList();

        if (sources.Count == 0)
        {
            sources.Add("Other");
        }

        SourceComboBox.ItemsSource = sources;
        SourceComboBox.SelectedIndex = 0;
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