// BA/Markup/Services/MarkupBaselineService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Markup.Models;
using BA.Markup.Settings;
using Newtonsoft.Json;

namespace BA.Markup.Services
{
    /// <summary>
    /// Owns "new since last sync" detection for markup notifications. Per-user, per-central
    /// baseline file at %AppData%\BA\Markup\Notifications\{CentralHash}\{Username}.json,
    /// same central-hash source as MarkupUserRegistryService (via MarkupCentralPathUtility),
    /// so both always agree on which central a given hash refers to.
    ///
    /// This is a purely local, per-machine file, unlike the registry it does not need to be
    /// shared across the team and is not written to the S:\ network root, there is nothing
    /// here another user would ever need to read.
    ///
    /// A missing or unreadable baseline is treated as "everything is new", not as an error,
    /// which is the correct behavior for a user's first sync after installing this feature.
    /// </summary>
    public static class MarkupBaselineService
    {
        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            Formatting = Formatting.Indented
        };

        /// <summary>
        /// Diffs rawItems (from MarkupScanService) against the user's last-seen baseline for
        /// this central, returns a new list where IsNew is set per item, and overwrites the
        /// baseline file with the current state so a repeat sync with no changes reports
        /// nothing as new. An item is IsNew if it was absent from the previous baseline, or
        /// its Wip/Solved state differs from what was last recorded.
        ///
        /// Never throws. On any failure to read or write the baseline, returns rawItems with
        /// IsNew left as false on every item (fails toward under-notifying, not toward
        /// spamming the user with a false "everything is new" on every sync if the baseline
        /// file is transiently locked).
        /// </summary>
        public static IReadOnlyList<MarkupNotificationItem> DiffAndUpdateBaseline(
            Document doc,
            string username,
            IReadOnlyList<MarkupNotificationItem> rawItems)
        {
            if (doc == null || string.IsNullOrWhiteSpace(username))
                return rawItems;

            string filePath = ResolveBaselineFilePath(doc, username);
            if (filePath == null)
                return rawItems;

            try
            {
                var previous = LoadBaseline(filePath);

                var diffed = rawItems
                    .Select(item =>
                    {
                        bool isNew = !previous.TryGetValue(item.ElementId, out var prior)
                            || prior.Wip != item.Wip
                            || prior.Solved != item.Solved;

                        return new MarkupNotificationItem
                        {
                            ElementId = item.ElementId,
                            OwnerViewId = item.OwnerViewId,
                            ViewName = item.ViewName,
                            AssignedUser = item.AssignedUser,
                            Author = item.Author,
                            Date = item.Date,
                            Comments = item.Comments,
                            BaType = item.BaType,
                            Wip = item.Wip,
                            Solved = item.Solved,
                            IsNew = isNew
                        };
                    })
                    .ToList();

                SaveBaseline(filePath, diffed);
                return diffed;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MarkupBaselineService.DiffAndUpdateBaseline", ex);
                return rawItems;
            }
        }

        // ------------------------------------------------------------------ //
        //  Path resolution
        // ------------------------------------------------------------------ //

        private static string ResolveBaselineFilePath(Document doc, string username)
        {
            string centralHash = MarkupCentralPathUtility.TryGetCentralHash(doc);
            if (centralHash == null)
                return null;

            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BA", "Markup", "Notifications", centralHash);

            try
            {
                if (!Directory.Exists(root))
                    Directory.CreateDirectory(root);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"MarkupBaselineService: could not create '{root}'", ex);
                return null;
            }

            // Username is a Windows/Revit login string; sanitize defensively before using
            // it as a filename component even though in practice it should already be safe.
            string safeUsername = string.Join("_", username.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(root, safeUsername + ".json");
        }

        // ------------------------------------------------------------------ //
        //  File I/O
        // ------------------------------------------------------------------ //

        private sealed class BaselineEntry
        {
            public long ElementId { get; set; }
            public bool Wip { get; set; }
            public bool Solved { get; set; }
        }

        private static Dictionary<long, BaselineEntry> LoadBaseline(string filePath)
        {
            var baseline = new Dictionary<long, BaselineEntry>();

            if (!File.Exists(filePath))
                return baseline;

            string json = File.ReadAllText(filePath);
            var entries = JsonConvert.DeserializeObject<List<BaselineEntry>>(json, JsonSettings);

            if (entries == null)
                return baseline;

            foreach (var entry in entries)
                baseline[entry.ElementId] = entry;

            return baseline;
        }

        private static void SaveBaseline(string filePath, IReadOnlyList<MarkupNotificationItem> items)
        {
            var entries = items
                .Select(i => new BaselineEntry
                {
                    ElementId = i.ElementId,
                    Wip = i.Wip,
                    Solved = i.Solved
                })
                .ToList();

            string json = JsonConvert.SerializeObject(entries, JsonSettings);

            string tempPath = filePath + ".tmp";
            File.WriteAllText(tempPath, json);

            if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                throw new IOException($"MarkupBaselineService: verification failed writing '{tempPath}'.");

            File.Copy(tempPath, filePath, overwrite: true);
            File.Delete(tempPath);
        }
    }
}