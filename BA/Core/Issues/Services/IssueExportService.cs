using BA.IssueReporter.Models;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BA.IssueReporter.Services;

public static class IssueExportService
{
    public static void ExportToCsv(IEnumerable<PluginIssue> issues, string filePath)
    {
        var sb = new StringBuilder();

        sb.AppendLine(
            "Number,Category,Status,Source,User,SubmittedAt,ProjectName,ProjectPath,Issue,Suggestion,ManagerComment,LastUpdatedBy,LastUpdatedAt");

        foreach (var issue in issues)
        {
            sb.AppendLine(string.Join(",",
                Csv(issue.DisplayNumber),
                Csv(issue.Category),
                Csv(issue.Status),
                Csv(issue.Source),
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
        value ??= string.Empty;
        value = value.Replace("\"", "\"\"");
        return $"\"{value}\"";
    }
}