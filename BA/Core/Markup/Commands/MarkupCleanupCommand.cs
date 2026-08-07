// BA/Markup/Commands/MarkupCleanupCommand.cs
using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Markup.Settings;

namespace BA.Markup.Commands
{
    /// <summary>
    /// Manual maintenance command. Purges markup-assignee registry entries older than
    /// MarkupSettings.MarkupCleanupRetentionMonths, then clears BA_Tls_AssignedUser on any
    /// BA_DetItem_Markup_RCP instance pointing at a purged username. Deliberately manual,
    /// not run automatically on sync, to avoid an extra document-wide collector pass on
    /// every user's machine on every SynchronizeWithCentral.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class MarkupCleanupCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var uiApp = commandData.Application;
            var doc = uiApp.ActiveUIDocument?.Document;

            if (doc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            var settings = MarkupSettings.Load<MarkupSettings>();

            IReadOnlyList<string> purgedUsers;
            try
            {
                purgedUsers = MarkupUserRegistryService.PurgeInactive(
                    doc, settings.MarkupCleanupRetentionMonths);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MarkupCleanupCommand.PurgeInactive", ex);
                TaskDialog.Show(
                    "BA Markup Cleanup - Error",
                    $"Failed to purge inactive users from the registry: {ex.Message}");
                return Result.Failed;
            }

            if (purgedUsers.Count == 0)
            {
                TaskDialog.Show(
                    "BA Markup Cleanup",
                    "No inactive users found. Nothing to clean up.");
                return Result.Succeeded;
            }

            var purgedSet = new HashSet<string>(purgedUsers, StringComparer.OrdinalIgnoreCase);
            int clearedCount;

            try
            {
                clearedCount = ClearAssignmentsForPurgedUsers(doc, settings, purgedSet);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MarkupCleanupCommand.ClearAssignmentsForPurgedUsers", ex);
                TaskDialog.Show(
                    "BA Markup Cleanup - Partial Failure",
                    $"Purged {purgedUsers.Count} inactive user(s) from the registry, " +
                    $"but failed while clearing their markup assignments: {ex.Message}\n\n" +
                    "The registry was updated; assignment clearing did not complete. " +
                    "Re-running this command will retry the clearing step, the purge itself " +
                    "will simply find nothing left to purge for these users.");
                return Result.Failed;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Purged {purgedUsers.Count} inactive user(s) from the registry:");
            foreach (var username in purgedUsers)
                sb.AppendLine($"  - {username}");
            sb.AppendLine();
            sb.AppendLine($"Cleared {clearedCount} markup assignment(s) pointing to purged users.");

            TaskDialog.Show("BA Markup Cleanup", sb.ToString());
            return Result.Succeeded;
        }

        /// <summary>
        /// Document-wide scan mirroring MarkupScanService's collector setup, but not
        /// reusing it directly, MarkupScanService filters to a single assigned user and
        /// excludes Solved items; this needs every instance regardless of Solved state,
        /// since a solved-but-still-assigned markup should still have its assignment
        /// cleared if the assignee was purged.
        /// </summary>
        private static int ClearAssignmentsForPurgedUsers(
            Document doc,
            Settings.MarkupSettings settings,
            HashSet<string> purgedUsers)
        {
            var toClear = new List<FamilyInstance>();

            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .OfClass(typeof(FamilyInstance));

            foreach (Element element in collector)
            {
                if (element is not FamilyInstance instance)
                    continue;

                string familyName = instance.Symbol?.Family?.Name;
                if (!string.Equals(familyName, settings.DetailItemFamilyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var assignedParam = instance.LookupParameter("BA.Tls_AssignedUser");
                if (assignedParam == null || !assignedParam.HasValue)
                    continue;

                string assignedUser = assignedParam.AsString() ?? string.Empty;
                if (string.IsNullOrEmpty(assignedUser))
                    continue;

                if (purgedUsers.Contains(assignedUser))
                    toClear.Add(instance);
            }

            if (toClear.Count == 0)
                return 0;

            using var tx = new Transaction(doc, "BA — Clear Inactive Markup Assignments");
            tx.Start();

            int cleared = 0;
            foreach (var instance in toClear)
            {
                var assignedParam = instance.LookupParameter("BA.Tls_AssignedUser");
                if (assignedParam == null || assignedParam.IsReadOnly)
                    continue;

                assignedParam.Set(string.Empty);
                cleared++;
            }

            tx.Commit();
            return cleared;
        }
    }
}