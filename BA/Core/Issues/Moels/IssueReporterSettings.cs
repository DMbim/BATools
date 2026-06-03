using System;
using System.Collections.Generic;
using System.IO;

namespace BA.IssueReporter.Models;

public class IssueReporterSettings
{
    public string IssueDatabasePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "BA",
        "IssueReporter",
        "issues.json");

    public string TeamsWorkflowUrl { get; set; } = string.Empty;

    public string CsvExportFolderPath { get; set; } =
        @"S:\CAD\Autodesk Revit\BA_Resources\BA_Issues\CSV";

    public List<string> ManagerUsers { get; set; } = new();
}