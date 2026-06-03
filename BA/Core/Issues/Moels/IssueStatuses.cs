namespace BA.IssueReporter.Models;

public static class IssueStatuses
{
    public const string New = "New";
    public const string Accepted = "Accepted";
    public const string InProgress = "In Progress";
    public const string NeedMoreInfo = "Need More Info";
    public const string Rejected = "Rejected";
    public const string Fixed = "Fixed";
    public const string Released = "Released";

    public static string[] All =>
    [
        New,
        Accepted,
        InProgress,
        NeedMoreInfo,
        Rejected,
        Fixed,
        Released
    ];
}
