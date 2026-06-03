using System.Collections.Generic;

namespace BA.IssueReporter.Models;

public static class IssueSources
{
    public static readonly string[] Template =
    {
        "Graphics",
        "View Templates",
        "Object Styles",
        "Line Styles",
        "Filters",
        "Missing Content",
        "Broken Content",
        "Wrong Content",
        "Change Feature",
        "Add Feature",
        "Remove Feature",
        "Documentation",
        "Other Template Issue"
    };

    public static readonly string[] Model =
    {
        "View Issue",
        "3D Model Issue",
        "Family Issue",
        "Link Issue",
        "Coordinates Issue",
        "Sheet Issue",
        "Schedule Issue",
        "Warning / Performance Issue",
        "BIM Issue",
        "Other Model Issue"
    };

    public static readonly string[] BIM =
    {
        "Project Start",
        "Naming",
        "Classification",
        "Parameters",
        "Shared Parameters",
        "Filters",
        "Dynamo",
        "Revit Standard",
        "Export / IFC",
        "Coordination",
        "Documentation",
        "Other BIM Issue"
    };

    public static readonly string[] Installer =
    {
        "Did Not Install",
        "Installed With Errors",
        "Update Failed",
        "Missing Buttons",
        "Missing Icons",
        "Wrong Version Installed",
        "Settings Missing",
        "Permission Issue",
        "Other Installer Issue"
    };

    public static readonly string[] Other =
    {
        "General Question",
        "Improvement Idea",
        "Training Request",
        "Documentation Request",
        "Other"
    };

    public static IReadOnlyList<string> GetForCategory(string category)
    {
        return category switch
        {
            IssueCategories.Template => Template,
            IssueCategories.Model => Model,
            IssueCategories.BIM => BIM,
            IssueCategories.Installer => Installer,
            IssueCategories.Other => Other,
            _ => Other
        };
    }
}