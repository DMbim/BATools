namespace BA.BAApplication.CommandRegistry;

internal sealed class BACommandInfo
{
    public string InternalName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string FullClassName { get; init; } = string.Empty;
    public string SmallIconPath { get; init; } = string.Empty;
    public string LargeIconPath { get; init; } = string.Empty;
    public bool ShowInIssueReporter { get; init; } = true;
}