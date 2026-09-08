// FILE: BA_Tools/Warnings/Services/FailureClassificationService.cs
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using BA.BAApplication;
using BA.Warnings.Models;

namespace BA.Warnings.Services
{
    // Loads the firm-maintained 1-5 failure classification table from the
    // network share, independent of Revit's native FailureSeverity, which in
    // practice is almost always Warning (Error/DocumentCorruption block
    // transaction commit before GetWarnings() ever surfaces them). This gives
    // the dashboard a real triage axis instead of one that collapses to a
    // single bucket for nearly every live warning.
    //
    // Deliberately a network share JSON file, not an embedded resource: this
    // table needs to grow as unclassified GUIDs get logged and reviewed, and
    // that should not require a rebuild/redeploy cycle, consistent with how
    // color schemes, filter groups, and markup users already live under
    // S:\CAD\Autodesk Revit\_admin\BA_tools\.
    public sealed class FailureClassificationService
    {
        public const string UnclassifiedCategory = "Unclassified";
        public const int UnclassifiedSeverity = 0;

        // ASSUMPTION FLAGGED: path convention only, one line to change if your
        // actual admin folder structure differs.
        private const string ClassificationFilePath =
            @"S:\CAD\Autodesk Revit\_admin\BA_tools\WarningsDashboard\FailureClassification.json";

        private static readonly object _lock = new object();
        private static Dictionary<Guid, FailureClassificationEntry> _cache;
        private static bool _loadAttempted;
        private static bool _lastLoadSucceeded;
        private static string _loadError;

        // Tracks GUIDs seen live with no classification entry, deduped per
        // session so a debounced live-refresh loop doesn't spam the log with
        // the same handful of unmatched warnings on every document change.
        private static readonly HashSet<Guid> _loggedUnmatched = new HashSet<Guid>();

        public static FailureClassificationService Instance { get; } = new FailureClassificationService();

        private FailureClassificationService() { }

        public bool LastLoadFailed => _loadAttempted && !_lastLoadSucceeded;
        public string LastLoadError => _loadError;

        public bool TryGetClassification(Guid guid, string liveDescriptionText, out FailureClassificationEntry entry)
        {
            EnsureLoaded();

            if (_cache.TryGetValue(guid, out entry))
                return true;

            entry = null;

            // Only log as a coverage gap if the table itself loaded successfully.
            // If the file is missing entirely, every GUID is "unmatched" and
            // logging each one as "add this to the file" would be misleading,
            // the real problem is the missing file, not a classification gap.
            if (_lastLoadSucceeded)
                LogUnmatchedOnce(guid, liveDescriptionText);

            return false;
        }

        // Exposed so a future "Reload Classification" button can force a
        // re-read after the network file is edited, without restarting Revit.
        public void Invalidate()
        {
            lock (_lock)
            {
                _cache = null;
                _loadAttempted = false;
                _lastLoadSucceeded = false;
                _loadError = null;
            }
        }

        private void EnsureLoaded()
        {
            if (_cache != null) return;

            lock (_lock)
            {
                if (_cache != null) return;
                if (_loadAttempted) return;
                _loadAttempted = true;

                try
                {
                    if (!File.Exists(ClassificationFilePath))
                    {
                        _loadError = $"Classification file not found at {ClassificationFilePath}.";
                        AppLogger.LogInfo($"FailureClassificationService: {_loadError} All warnings will show as Unclassified.");
                        _cache = new Dictionary<Guid, FailureClassificationEntry>();
                        _lastLoadSucceeded = false;
                        return;
                    }

                    string json = File.ReadAllText(ClassificationFilePath);
                    List<FailureClassificationEntry> entries =
                        JsonConvert.DeserializeObject<List<FailureClassificationEntry>>(json)
                        ?? new List<FailureClassificationEntry>();

                    var dict = new Dictionary<Guid, FailureClassificationEntry>();
                    foreach (FailureClassificationEntry e in entries)
                    {
                        // Last one wins on a duplicate GUID rather than throwing.
                        // A malformed edit to the shared file shouldn't take the
                        // whole dashboard down for everyone in the office.
                        dict[e.Guid] = e;
                    }

                    _cache = dict;
                    _lastLoadSucceeded = true;
                    AppLogger.LogInfo($"FailureClassificationService: loaded {dict.Count} classification entries from {ClassificationFilePath}.");
                }
                catch (Exception ex)
                {
                    _loadError = ex.Message;
                    AppLogger.LogError("FailureClassificationService.EnsureLoaded", ex);
                    _cache = new Dictionary<Guid, FailureClassificationEntry>();
                    _lastLoadSucceeded = false;
                }
            }
        }

        private void LogUnmatchedOnce(Guid guid, string liveDescriptionText)
        {
            lock (_lock)
            {
                if (_loggedUnmatched.Contains(guid)) return;
                _loggedUnmatched.Add(guid);
            }

            AppLogger.LogInfo($"FailureClassificationService: unclassified FailureDefinitionId {guid} seen live. " +
                               $"Description: \"{liveDescriptionText}\". Add this to FailureClassification.json to classify it.");
        }
    }
}