// BA/Markup/Services/MarkupScanService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Markup.Models;
using BA.Markup.Settings;

namespace BA.Markup.Services
{
    /// <summary>
    /// Document-wide scan for BA_DetItem_Markup_RCP instances assigned to a specific user.
    /// Not view-scoped, deliberately: an assigned markup could be on any sheet or view, and
    /// a view-scoped FilteredElementCollector silently excludes hidden elements anyway (see
    /// this project's confirmed Revit API learnings).
    ///
    /// Excludes BA_Tls_Solved == true items at the source; MarkupNotificationItem.Solved is
    /// still populated on the DTO so the ViewModel can reflect a MarkSolvedCommand click
    /// locally without forcing a full rescan.
    ///
    /// Has no baseline/IsNew awareness of its own. MarkupBaselineService is responsible for
    /// diffing this scan's output against the user's last-seen state and producing the final
    /// IsNew-flagged list actually shown in the notification window.
    /// </summary>
    public static class MarkupScanService
    {
        /// <summary>
        /// Scans doc for BA_DetItem_Markup_RCP instances where BA_Tls_AssignedUser matches
        /// currentUsername (ordinal, case-insensitive) and BA_Tls_Solved is false. Returns
        /// an empty list on any failure rather than throwing, the notification handler
        /// should treat a scan failure as "nothing to show", not a crash.
        /// </summary>
        public static IReadOnlyList<MarkupNotificationItem> ScanForUser(Document doc, string currentUsername)
        {
            if (doc == null || string.IsNullOrWhiteSpace(currentUsername))
                return Array.Empty<MarkupNotificationItem>();

            var settings = MarkupSettings.Load<MarkupSettings>();
            var results = new List<MarkupNotificationItem>();

            try
            {
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

                    string assignedUser = GetStringParam(instance, "BA.Tls_AssignedUser");
                    if (!string.Equals(assignedUser, currentUsername, StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool solved = GetBoolParam(instance, "BA.Tls_Solved");
                    if (solved)
                        continue;

                    string viewName = string.Empty;
                    if (instance.OwnerViewId != ElementId.InvalidElementId)
                    {
                        if (doc.GetElement(instance.OwnerViewId) is View ownerView)
                            viewName = ownerView.Name;
                    }

                    results.Add(new MarkupNotificationItem
                    {
                        ElementId = instance.Id.Value,
                        OwnerViewId = instance.OwnerViewId.Value,
                        ViewName = viewName,
                        AssignedUser = assignedUser,
                        Author = GetStringParam(instance, "BA_Markup_Author"),
                        Date = GetStringParam(instance, "BA_Markup_Date"),
                        Comments = GetStringParam(instance, "BA_Comments"),
                        BaType = GetStringParam(instance, "BA_Type"),
                        Wip = GetBoolParam(instance, "BA.Tls_WIP"),
                        Solved = solved,
                        IsNew = false // set later by MarkupBaselineService
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MarkupScanService.ScanForUser", ex);
                return Array.Empty<MarkupNotificationItem>();
            }

            return results;
        }

        private static string GetStringParam(Element element, string paramName)
        {
            var p = element.LookupParameter(paramName);
            return p != null && p.HasValue ? (p.AsString() ?? string.Empty) : string.Empty;
        }

        private static bool GetBoolParam(Element element, string paramName)
        {
            var p = element.LookupParameter(paramName);
            return p != null && p.HasValue && p.AsInteger() == 1;
        }
    }
}