namespace BA.IssueReporter.Models;

public static class IssueCategories
{
    public const string Plugin = "Plugin";
    public const string Template = "Template";
    public const string Model = "Model";
    public const string BIM = "BIM";
    public const string Installer = "Installer";
    public const string Other = "Other";

    public static readonly string[] All =
    {
        Plugin,
        Template,
        Model,
        BIM,
        Installer,
        Other
    };

    public static string GetPrefix(string category)
    {
        return category switch
        {
            Plugin => "ISPI",
            Template => "ISTE",
            Model => "ISMO",
            BIM => "ISBI",
            Installer => "ISIN",
            Other => "ISOT",
            _ => "ISOT"
        };
    }
}