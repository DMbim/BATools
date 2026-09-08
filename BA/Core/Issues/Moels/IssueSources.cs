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
        "Line Patterns",
        "Fill Patterns",
        "Filters",
        "Title Blocks",
        "Legends",
        "Keynotes",
        "Tags",
        "Annotation Families",
        "Dimension Styles",
        "Text Styles",
        "Sheet Setup",
        "Browser Organization",
        "Worksets",
        "Phases",
        "Materials",
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
        "Room / Area Issue",
        "Wall Issue",
        "Floor / Ceiling Issue",
        "Structural Issue",
        "MEP Issue",
        "Site / Topography Issue",
        "Detailing Issue",
        "Annotation Issue",
        "Clash / Coordination Issue",
        "Duplicate Elements",
        "Missing Elements",
        "Wrong Type Or Family Used",
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
        "Worksharing / Worksets",
        "Model Health / Performance",
        "Point Cloud",
        "Federated Model",
        "Level Of Development (LOD)",
        "COBie",
        "QA / QC Checks",
        "Standards Compliance",
        "Template Compliance",
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
        "Ribbon Not Loading",
        "Plugin Not Found In Revit",
        "Wrong Version Installed",
        "Settings Missing",
        "Permission Issue",
        "License / Activation Issue",
        "Conflicting Add In",
        "Uninstall Failed",
        "Network / Deployment Path Issue",
        "Antivirus Blocked Install",
        "Other Installer Issue"
    };

    public static readonly string[] Other =
    {
        "General Question",
        "Improvement Idea",
        "Feature Request",
        "Workflow Suggestion",
        "Training Request",
        "Documentation Request",
        "Feedback",
        "Bug Report (Uncategorized)",
        "Other"
    };

    // NEW: generic, non command specific entries appended after the live BACommandRegistry
    // list when Category is Plugin, so a report is not forced onto a single command name.
    public static readonly string[] PluginGeneric =
    {
        "Performance / Slow",
        "Crash / Unhandled Exception",
        "Ribbon / UI Issue",
        "Installation Issue",
        "Other Plugin Issue"
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