using BA.IssueReporter.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace BA.IssueReporter.Services
{
    public class IssueStorageService
    {
        private readonly string _databasePath;

        public IssueStorageService(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentException(
                    "Issue database path is null or empty. Check IssueDatabasePath in settings.json.",
                    nameof(databasePath));
            }

            _databasePath = databasePath;
            EnsureStorageExists();
        }

        private void EnsureStorageExists()
        {
            if (string.IsNullOrWhiteSpace(_databasePath))
            {
                throw new InvalidOperationException(
                    "Issue database path is null or empty.");
            }

            string folder = Path.GetDirectoryName(_databasePath);

            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new InvalidOperationException(
                    $"Could not determine folder from IssueDatabasePath:\n{_databasePath}");
            }

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            if (!File.Exists(_databasePath))
            {
                File.WriteAllText(_databasePath, "[]");
            }
        }

        public List<PluginIssue> LoadIssues()
        {
            EnsureStorageExists();

            string json = File.ReadAllText(_databasePath);

            if (string.IsNullOrWhiteSpace(json))
                return new List<PluginIssue>();

            try
            {
                return JsonSerializer.Deserialize<List<PluginIssue>>(json)
                       ?? new List<PluginIssue>();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not read issue database JSON:\n{_databasePath}\n\n{ex.Message}",
                    ex);
            }
        }

        public void SaveIssues(List<PluginIssue> issues)
        {
            EnsureStorageExists();

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(issues, options);

            File.WriteAllText(_databasePath, json);
        }

        public void AddIssue(PluginIssue issue)
        {
            if (issue == null)
                throw new ArgumentNullException(nameof(issue));

            var issues = LoadIssues();

            if (string.IsNullOrWhiteSpace(issue.Category))
            {
                issue.Category = IssueCategories.Other;
            }

            if (issue.Number <= 0)
            {
                int maxNumberInCategory = issues
                    .Where(x => string.Equals(
                        x.Category,
                        issue.Category,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Number)
                    .DefaultIfEmpty(0)
                    .Max();

                issue.Number = maxNumberInCategory + 1;
            }

            issues.Add(issue);
            SaveIssues(issues);
        }

        public void UpdateIssue(PluginIssue updatedIssue)
        {
            if (updatedIssue == null)
                throw new ArgumentNullException(nameof(updatedIssue));

            var issues = LoadIssues();

            var existing = issues.FirstOrDefault(x => x.Id == updatedIssue.Id);

            if (existing == null)
                throw new InvalidOperationException("Issue was not found in the issue database.");

            existing.Command = updatedIssue.Command;
            existing.Issue = updatedIssue.Issue;
            existing.Suggestion = updatedIssue.Suggestion;
            existing.User = updatedIssue.User;
            existing.SubmittedAt = updatedIssue.SubmittedAt;
            existing.ProjectName = updatedIssue.ProjectName;
            existing.ProjectPath = updatedIssue.ProjectPath;
            existing.Status = updatedIssue.Status;
            existing.ManagerComment = updatedIssue.ManagerComment;
            existing.LastUpdatedBy = updatedIssue.LastUpdatedBy;
            existing.LastUpdatedAt = updatedIssue.LastUpdatedAt;

            SaveIssues(issues);
        }
        public static void ExportToCsv(IEnumerable<PluginIssue> issues, string filePath)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Number,Category,Status,Command,User,SubmittedAt,ProjectName,ProjectPath,Issue,Suggestion,ManagerComment,LastUpdatedBy,LastUpdatedAt");

            foreach (var issue in issues)
            {
                sb.AppendLine(string.Join(",",
                    Csv(issue.DisplayNumber),
                    Csv(issue.Category),
                    Csv(issue.Status),
                    Csv(issue.Command),
                    Csv(issue.User),
                    Csv(issue.SubmittedAt.ToString("yyyy-MM-dd HH:mm")),
                    Csv(issue.ProjectName),
                    Csv(issue.ProjectPath),
                    Csv(issue.Issue),
                    Csv(issue.Suggestion),
                    Csv(issue.ManagerComment),
                    Csv(issue.LastUpdatedBy),
                    Csv(issue.LastUpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "")
                ));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static string Csv(string value)
        {
            value ??= "";
            value = value.Replace("\"", "\"\"");
            return $"\"{value}\"";
        }
    }
}