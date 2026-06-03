using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.BAApplication.CommandRegistry;

internal static class BACommandRegistry
{
    private static readonly List<BACommandInfo> _commands = new();

    public static IReadOnlyList<BACommandInfo> All => _commands;

    public static void Register(BACommandInfo command)
    {
        if (command == null)
            return;

        if (string.IsNullOrWhiteSpace(command.InternalName))
            return;

        bool exists = _commands.Any(x =>
            string.Equals(x.InternalName, command.InternalName, StringComparison.OrdinalIgnoreCase));

        if (exists)
            return;

        _commands.Add(command);
    }

    public static List<string> GetIssueReporterCommandNames()
    {
        return _commands
            .Where(x => x.ShowInIssueReporter)
            .OrderBy(x => x.Category)
            .ThenBy(x => x.DisplayName)
            .Select(x => x.DisplayName)
            .Distinct()
            .ToList();
    }

    private static bool IsIssueReporterInternalCommand(string internalName)
    {
        return internalName.Equals("Issues", StringComparison.OrdinalIgnoreCase)
            || internalName.Equals("ManageIssues", StringComparison.OrdinalIgnoreCase)
            || internalName.Equals("SubmitIssue", StringComparison.OrdinalIgnoreCase)
            || internalName.Equals("IssueReporterSettings", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldShowInIssueReporter(string internalName)
    {
        return !IsIssueReporterInternalCommand(internalName);
    }
}