using System;

namespace BA.IssueReporter.Models;

public class PluginIssue
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // Category-based human-readable number.
    // Example:
    // Plugin   -> ISPI-0001
    // Template -> ISTE-0001
    // Model    -> ISMO-0001
    public int Number { get; set; }

    public string DisplayNumber => $"{IssueCategories.GetPrefix(Category)}-{Number:0000}";

    // User-filled
    public string Category { get; set; } = IssueCategories.Plugin;

    // Keep this property name for existing JSON compatibility.
    // UI should display this as "Source".
    public string Command { get; set; } = string.Empty;

    public string Source
    {
        get => Command;
        set => Command = value;
    }

    public string Issue { get; set; } = string.Empty;
    public string Suggestion { get; set; } = string.Empty;

    // Auto-filled
    public string User { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; } = DateTime.Now;
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;

    // Manager-filled
    public string Status { get; set; } = IssueStatuses.New;
    public string ManagerComment { get; set; } = string.Empty;
    public string LastUpdatedBy { get; set; } = string.Empty;
    public DateTime? LastUpdatedAt { get; set; }
}