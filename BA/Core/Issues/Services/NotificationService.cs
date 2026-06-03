using BA.IssueReporter.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BA.IssueReporter.Services;

public class NotificationService
{
    private readonly string _workflowUrl;

    public NotificationService(string workflowUrl)
    {
        _workflowUrl = workflowUrl;
    }

    public async Task NotifyIssueSubmittedAsync(PluginIssue issue)
    {
        if (string.IsNullOrWhiteSpace(_workflowUrl))
            return;

        var card = BuildIssueSubmittedAdaptiveCard(issue);
        await PostJsonAsync(card);
    }

    public async Task NotifyIssueUpdatedAsync(PluginIssue issue)
    {
        if (string.IsNullOrWhiteSpace(_workflowUrl))
            return;

        var card = BuildIssueUpdatedAdaptiveCard(issue);
        await PostJsonAsync(card);
    }

    private async Task PostJsonAsync(object payload)
    {
        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = false
        });

        using var client = new HttpClient();

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await client.PostAsync(_workflowUrl, content);

        string responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Teams Workflow request failed.\n\n" +
                $"Status: {(int)response.StatusCode} {response.ReasonPhrase}\n\n" +
                $"Response:\n{responseText}");
        }
    }

    private object BuildIssueSubmittedAdaptiveCard(PluginIssue issue)
    {
        return new Dictionary<string, object>
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.4",
            ["body"] = new object[]
            {
                TextBlock($"New BA Issue: {issue.DisplayNumber}", "Bolder", "Large"),
                TextBlock($"Source: {Safe(issue.Source)}", "Bolder", "Medium"),

                new Dictionary<string, object>
                {
                    ["type"] = "FactSet",
                    ["facts"] = new object[]
                    {
                        Fact("Number", issue.DisplayNumber),
                        Fact("Category", Safe(issue.Category)),
                        Fact("Status", Safe(issue.Status)),
                        Fact("User", Safe(issue.User)),
                        Fact("Project", Safe(issue.ProjectName)),
                        Fact("Submitted", issue.SubmittedAt.ToString("yyyy-MM-dd HH:mm"))
                    }
                },

                TextBlock("Issue", "Bolder", "Medium", spacing: "Medium"),
                TextBlock(Safe(issue.Issue)),

                TextBlock("Suggestion", "Bolder", "Medium", spacing: "Medium"),
                TextBlock(string.IsNullOrWhiteSpace(issue.Suggestion) ? "-" : Safe(issue.Suggestion)),

                TextBlock($"Project path: {Safe(issue.ProjectPath)}", isSubtle: true, spacing: "Medium")
            }
        };
    }

    private object BuildIssueUpdatedAdaptiveCard(PluginIssue issue)
    {
        return new Dictionary<string, object>
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.4",
            ["body"] = new object[]
            {
                TextBlock($"BA Issue Updated: {issue.DisplayNumber}", "Bolder", "Large"),

                TextBlock($"Source: {Safe(issue.Source)}", "Bolder", "Medium"),

                new Dictionary<string, object>
                {
                    ["type"] = "FactSet",
                    ["facts"] = new object[]
                    {
                        Fact("Number", issue.DisplayNumber),
                        Fact("Category", Safe(issue.Category)),
                        Fact("Status", Safe(issue.Status)),
                        Fact("User", Safe(issue.User)),
                        Fact("Project", Safe(issue.ProjectName)),
                        Fact("Updated by", Safe(issue.LastUpdatedBy)),
                        Fact(
                            "Updated",
                            issue.LastUpdatedAt.HasValue
                                ? issue.LastUpdatedAt.Value.ToString("yyyy-MM-dd HH:mm")
                                : "-")
                    }
                },

                TextBlock("Manager Comment", "Bolder", "Medium", spacing: "Medium"),
                TextBlock(string.IsNullOrWhiteSpace(issue.ManagerComment) ? "-" : Safe(issue.ManagerComment))
            }
        };
    }

    private static Dictionary<string, object> TextBlock(
        string text,
        string weight = null,
        string size = null,
        string spacing = null,
        bool isSubtle = false)
    {
        var block = new Dictionary<string, object>
        {
            ["type"] = "TextBlock",
            ["text"] = Safe(text),
            ["wrap"] = true
        };

        if (!string.IsNullOrWhiteSpace(weight))
            block["weight"] = weight;

        if (!string.IsNullOrWhiteSpace(size))
            block["size"] = size;

        if (!string.IsNullOrWhiteSpace(spacing))
            block["spacing"] = spacing;

        if (isSubtle)
            block["isSubtle"] = true;

        return block;
    }

    private static Dictionary<string, object> Fact(string title, string value)
    {
        return new Dictionary<string, object>
        {
            ["title"] = Safe(title),
            ["value"] = Safe(value)
        };
    }

    private static string Safe(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }
}