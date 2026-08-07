// BA/Markup/Settings/MarkupUserRegistryService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.Ledger;
using Newtonsoft.Json;

namespace BA.Markup.Settings
{
    /// <summary>
    /// Owns the per-central registry of users eligible to be assigned a markup, i.e. anyone
    /// who has successfully synced this central within MarkupSettings.MarkupCleanupRetentionMonths.
    ///
    /// Independent of the Type Data Ledger by design. Participation is self-recorded: every
    /// successful SynchronizeWithCentral calls RecordParticipation directly, there is no
    /// element scanning and no dependency on the Ledger being enabled for this project.
    ///
    /// Storage: one JSON file per unique central, at
    /// {MarkupSettings.MarkupUserRegistryRoot}\{ProjectSet}\{CentralHash}.json
    /// ProjectSet comes from BA.Core.Ledger.ProjectSetService.GetProjectSetName(doc); if that
    /// returns null the registry falls back to a "_NoProjectSet" folder rather than failing.
    /// CentralHash comes from MarkupCentralPathUtility, shared with MarkupBaselineService so
    /// both always agree on which central a given hash refers to.
    ///
    /// If the network root is unreachable, falls back to a local mirror under
    /// %AppData%\BA\Markup\UserRegistry\, matching this project's established network-path
    /// resilience convention. In that fallback state the registry is no longer actually
    /// shared across the team for that session; this is logged via AppLogger every time it
    /// happens so it doesn't fail silently.
    /// </summary>
    public static class MarkupUserRegistryService
    {
        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            Formatting = Formatting.Indented
        };

        // ------------------------------------------------------------------ //
        //  Public API
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Records that the current user has just successfully synced this central.
        /// Call from the SynchronizeWithCentral success path, after the sync completes.
        /// Updates LastSeenUtc in place if the user already has an entry, adds a new entry
        /// otherwise. Never throws; failures are logged and swallowed, a missed participation
        /// record is not worth failing the user's sync operation over.
        /// </summary>
        public static void RecordParticipation(Document doc, string username)
        {
            if (doc == null || string.IsNullOrWhiteSpace(username))
                return;

            try
            {
                var filePath = ResolveRegistryFilePath(doc);
                if (filePath == null) return;

                var registry = LoadRegistry(filePath);

                registry[username] = new Models.MarkupUserRegistryEntry
                {
                    Username = username,
                    LastSeenUtc = DateTime.UtcNow
                };

                SaveRegistry(filePath, registry);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MarkupUserRegistryService.RecordParticipation", ex);
            }
        }

        /// <summary>
        /// Returns usernames considered active for this central, i.e. LastSeenUtc within
        /// maxAgeMonths of now. Used by the Markup assignee picker. Never throws; returns
        /// an empty list on any failure so the picker degrades to free-text-only rather
        /// than blocking the dialog.
        /// </summary>
        public static IReadOnlyList<string> GetActiveUsers(Document doc, int maxAgeMonths)
        {
            try
            {
                var filePath = ResolveRegistryFilePath(doc);
                if (filePath == null) return Array.Empty<string>();

                var registry = LoadRegistry(filePath);
                var cutoff = DateTime.UtcNow.AddMonths(-Math.Max(0, maxAgeMonths));

                return registry.Values
                    .Where(e => e.LastSeenUtc >= cutoff)
                    .Select(e => e.Username)
                    .OrderBy(u => u, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MarkupUserRegistryService.GetActiveUsers", ex);
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Removes entries with LastSeenUtc older than maxAgeMonths from the registry file
        /// and returns the usernames that were purged, so MarkupCleanupCommand can go clear
        /// BA_Tls_AssignedUser on any markup pointing at them. Does not touch any markup
        /// elements itself, this method only mutates the registry file. Returns an empty
        /// list, not null, if nothing was purged or the operation failed; callers should not
        /// need to null-check.
        /// </summary>
        public static IReadOnlyList<string> PurgeInactive(Document doc, int maxAgeMonths)
        {
            try
            {
                var filePath = ResolveRegistryFilePath(doc);
                if (filePath == null) return Array.Empty<string>();

                var registry = LoadRegistry(filePath);
                var cutoff = DateTime.UtcNow.AddMonths(-Math.Max(0, maxAgeMonths));

                var purged = registry.Values
                    .Where(e => e.LastSeenUtc < cutoff)
                    .Select(e => e.Username)
                    .ToList();

                if (purged.Count == 0)
                    return Array.Empty<string>();

                foreach (var username in purged)
                    registry.Remove(username);

                SaveRegistry(filePath, registry);
                return purged;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MarkupUserRegistryService.PurgeInactive", ex);
                return Array.Empty<string>();
            }
        }

        // ------------------------------------------------------------------ //
        //  Path resolution
        // ------------------------------------------------------------------ //

        private static string ResolveRegistryFilePath(Document doc)
        {
            var settings = MarkupSettings.Load<MarkupSettings>();

            string projectSet = ProjectSetService.GetProjectSetName(doc);
            if (string.IsNullOrWhiteSpace(projectSet))
                projectSet = "_NoProjectSet";

            string centralHash = MarkupCentralPathUtility.TryGetCentralHash(doc);
            if (centralHash == null)
                return null;

            string fileName = centralHash + ".json";

            string networkDir = Path.Combine(settings.MarkupUserRegistryRoot, projectSet);
            if (TryEnsureDirectory(networkDir))
                return Path.Combine(networkDir, fileName);

            string localRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BA", "Markup", "UserRegistry");
            string localDir = Path.Combine(localRoot, projectSet);

            AppLogger.LogInfo(
                $"MarkupUserRegistryService: network root '{networkDir}' unreachable, " +
                $"falling back to local mirror '{localDir}'. Registry is not shared across " +
                "the team while this fallback is active.");

            TryEnsureDirectory(localDir);
            return Path.Combine(localDir, fileName);
        }

        private static bool TryEnsureDirectory(string dir)
        {
            try
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ //
        //  File I/O
        // ------------------------------------------------------------------ //

        private static Dictionary<string, Models.MarkupUserRegistryEntry> LoadRegistry(string filePath)
        {
            var registry = new Dictionary<string, Models.MarkupUserRegistryEntry>(
                StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(filePath))
                return registry;

            try
            {
                string json = File.ReadAllText(filePath);
                var entries = JsonConvert.DeserializeObject<List<Models.MarkupUserRegistryEntry>>(
                    json, JsonSettings);

                if (entries == null)
                    return registry;

                // Re-apply OrdinalIgnoreCase explicitly. Confirmed project-wide learning:
                // JSON dictionary comparers are lost after deserialization, so this registry
                // is stored as a flat List<T> on disk and rebuilt into a case-insensitive
                // Dictionary here, never deserialized directly into a Dictionary.
                foreach (var entry in entries)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Username))
                        registry[entry.Username] = entry;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"MarkupUserRegistryService.LoadRegistry '{filePath}'", ex);
            }

            return registry;
        }

        private static void SaveRegistry(
            string filePath,
            Dictionary<string, Models.MarkupUserRegistryEntry> registry)
        {
            var entries = registry.Values.ToList();
            string json = JsonConvert.SerializeObject(entries, JsonSettings);

            string tempPath = filePath + ".tmp";
            File.WriteAllText(tempPath, json);

            // Verify write succeeded before replacing the real file. Consistent with the
            // project's confirmed learning that silent write failures corrupt baseline
            // state; a corrupted or half-written registry silently un-eligible-izes every
            // active user on this central until someone notices.
            if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                throw new IOException($"MarkupUserRegistryService: verification failed writing '{tempPath}'.");

            File.Copy(tempPath, filePath, overwrite: true);
            File.Delete(tempPath);
        }
    }
}