// File: BA/Application/AutoSave/AutoSaveService.cs
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using BA.Core.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;

namespace BA.BAApplication.AutoSave
{
    /// <summary>
    /// Silent, forced autosave for open project documents.
    ///
    /// Threading: everything here runs on the Revit main thread. The
    /// DispatcherTimer ticks on the dispatcher captured in Register (Revit's
    /// UI thread), document events are raised on that thread, and the
    /// ExternalEvent handler executes there too, so no locking is needed.
    /// The timer tick is NOT a Revit API context: it only reads the in memory
    /// tracking table and never touches a Document. All API access happens in
    /// RunSavePass, inside the ExternalEvent handler.
    ///
    /// Timing: each document's clock measures time since it was last in a
    /// saved state. It resets on open, on any save (manual, autosave, Save As),
    /// on Synchronize with Central, and whenever a due check finds the
    /// document unmodified, because an unmodified document is by definition
    /// in its saved state at that moment.
    ///
    /// Workshared: Document.Save() on a local file saves the local only.
    /// Synchronize with Central is never automated.
    /// </summary>
    internal static class AutoSaveService
    {
        // How often the timer looks for due documents. Not the save interval,
        // which comes from AutoSaveGate.IntervalMinutes.
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);

        // Back off after a deferred or failed attempt (open transaction, edit
        // mode, temporary read only state, save exception) so the user is not
        // interrupted and the log is not written every tick during a sketch.
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(2);

        // Every save of a non workshared cloud model publishes a new version
        // to Docs. Off to avoid hourly version churn. Set true to include them.
        private static readonly bool SaveNonWorksharedCloudModels = false;

        // Keyed by Document.PathName. API Document wrappers are recreated on
        // every access, so reference identity cannot be used as a key.
        private static readonly Dictionary<string, TrackedDocument> Tracked =
            new Dictionary<string, TrackedDocument>(StringComparer.OrdinalIgnoreCase);

        private static Autodesk.Revit.ApplicationServices.ControlledApplication? _controlledApp;
        private static AutoSaveExternalEventHandler? _handler;
        private static Autodesk.Revit.UI.ExternalEvent? _externalEvent;
        private static DispatcherTimer? _timer;

        /// <summary>
        /// Call from IExternalApplication.OnStartup. OnStartup is a valid API
        /// context for ExternalEvent.Create, and runs on the Revit UI thread,
        /// so Dispatcher.CurrentDispatcher is Revit's dispatcher.
        /// </summary>
        public static void Register(UIControlledApplication application)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            if (_externalEvent != null) return;

            _handler = new AutoSaveExternalEventHandler();
            _externalEvent = Autodesk.Revit.UI.ExternalEvent.Create(_handler);

            _controlledApp = application.ControlledApplication;
            _controlledApp.DocumentOpened += OnDocumentOpened;
            _controlledApp.DocumentSaved += OnDocumentSaved;
            _controlledApp.DocumentSavedAs += OnDocumentSavedAs;
            _controlledApp.DocumentSynchronizedWithCentral += OnDocumentSynchronizedWithCentral;
            _controlledApp.DocumentClosing += OnDocumentClosing;

            _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher)
            {
                Interval = TickInterval
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();

            AppLogger.LogInfo(
                $"Autosave registered. Enabled: {AutoSaveGate.Enabled}, interval: {AutoSaveGate.IntervalMinutes} min.");
        }

        /// <summary>
        /// Call from IExternalApplication.OnShutdown.
        /// </summary>
        public static void Unregister()
        {
            try
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Tick -= OnTimerTick;
                    _timer = null;
                }

                if (_controlledApp != null)
                {
                    _controlledApp.DocumentOpened -= OnDocumentOpened;
                    _controlledApp.DocumentSaved -= OnDocumentSaved;
                    _controlledApp.DocumentSavedAs -= OnDocumentSavedAs;
                    _controlledApp.DocumentSynchronizedWithCentral -= OnDocumentSynchronizedWithCentral;
                    _controlledApp.DocumentClosing -= OnDocumentClosing;
                    _controlledApp = null;
                }

                _externalEvent?.Dispose();
                _externalEvent = null;
                _handler = null;

                Tracked.Clear();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("AutoSaveService.Unregister", ex);
            }
        }

        // ---------------------------------------------------------------
        // Timer (NOT an API context: no Document access here)
        // ---------------------------------------------------------------

        private static void OnTimerTick(object? sender, EventArgs e)
        {
            try
            {
                if (!AutoSaveGate.Enabled) return;
                if (_externalEvent == null || _externalEvent.IsPending) return;
                if (Tracked.Count == 0) return;

                DateTime now = DateTime.UtcNow;
                TimeSpan interval = TimeSpan.FromMinutes(AutoSaveGate.IntervalMinutes);

                if (!Tracked.Values.Any(t => t.IsDue(now, interval))) return;

                ExternalEventRequest request = _externalEvent.Raise();
                if (request == ExternalEventRequest.Denied || request == ExternalEventRequest.TimedOut)
                {
                    AppLogger.LogInfo($"Autosave: ExternalEvent.Raise returned {request}, retrying on the next tick.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("AutoSaveService.OnTimerTick", ex);
            }
        }

        // ---------------------------------------------------------------
        // Save pass (API context, called from AutoSaveExternalEventHandler)
        // ---------------------------------------------------------------

        internal static void RunSavePass(UIApplication uiApp)
        {
            if (uiApp == null || !AutoSaveGate.Enabled) return;

            DateTime now = DateTime.UtcNow;
            TimeSpan interval = TimeSpan.FromMinutes(AutoSaveGate.IntervalMinutes);
            var openPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Autodesk.Revit.DB.Document doc in uiApp.Application.Documents)
            {
                string path;
                try
                {
                    if (!doc.IsValidObject) continue;
                    path = doc.PathName ?? "";
                }
                catch
                {
                    continue;
                }

                // Never saved, or detached: Save() would throw. Picked up by
                // DocumentSavedAs once the user gives it a path.
                if (string.IsNullOrWhiteSpace(path)) continue;

                openPaths.Add(path);

                if (!Tracked.TryGetValue(path, out TrackedDocument? tracked))
                {
                    // Missed by the events for some reason: start its clock now
                    // rather than saving it immediately.
                    Tracked[path] = CreateTracked(doc, now);
                    continue;
                }

                if (!tracked.IsDue(now, interval)) continue;

                TrySave(doc, tracked, now);
            }

            // Drop entries for documents that are no longer open (closing was
            // cancelled after removal, paths changed by Save As, and so on).
            foreach (string stale in Tracked.Keys.Where(k => !openPaths.Contains(k)).ToList())
            {
                Tracked.Remove(stale);
            }
        }

        private static void TrySave(Autodesk.Revit.DB.Document doc, TrackedDocument tracked, DateTime now)
        {
            string title = SafeTitle(doc);

            string? transientReason;
            try
            {
                transientReason = GetTransientSkipReason(doc);
            }
            catch (Exception ex)
            {
                tracked.DeferRetry(now + RetryDelay);
                if (tracked.ConsecutiveDeferrals == 1)
                    AppLogger.LogError($"Autosave: could not inspect '{title}'", ex);
                return;
            }

            if (transientReason != null)
            {
                tracked.DeferRetry(now + RetryDelay);
                if (tracked.ConsecutiveDeferrals == 1)
                {
                    AppLogger.LogInfo(
                        $"Autosave: '{title}' deferred, {transientReason}. Retrying every {RetryDelay.TotalMinutes:0} min.");
                }
                return;
            }

            if (!doc.IsModified)
            {
                tracked.MarkSavedState(now);
                return;
            }

            int deferralsBeforeSave = tracked.ConsecutiveDeferrals;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                doc.Save();
                stopwatch.Stop();

                // DocumentSaved has already reset the clock synchronously during
                // Save(); this keeps the state correct even if that event was missed.
                tracked.MarkSavedState(DateTime.UtcNow);

                string suffix = deferralsBeforeSave > 0
                    ? $" after {deferralsBeforeSave} deferred attempt(s)"
                    : "";

                AppLogger.LogInfo($"Autosave: saved '{title}' in {stopwatch.Elapsed.TotalSeconds:0.0} s{suffix}.");
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                // Typical cause: the document is in an edit mode (sketch, group
                // edit, in place family) or otherwise cannot be saved right now.
                tracked.DeferRetry(DateTime.UtcNow + RetryDelay);
                if (tracked.ConsecutiveDeferrals == 1)
                {
                    AppLogger.LogInfo(
                        $"Autosave: '{title}' could not be saved right now ({ex.Message}). Retrying every {RetryDelay.TotalMinutes:0} min.");
                }
            }
            catch (Exception ex)
            {
                tracked.DeferRetry(DateTime.UtcNow + RetryDelay);
                if (tracked.ConsecutiveDeferrals == 1)
                {
                    AppLogger.LogError($"Autosave: saving '{title}' failed", ex);
                }
            }
        }

        // ---------------------------------------------------------------
        // Document events (raised on the Revit main thread)
        // ---------------------------------------------------------------

        private static void OnDocumentOpened(object? sender, DocumentOpenedEventArgs e)
        {
            if (e.Status != RevitAPIEventStatus.Succeeded) return;
            RefreshFromEvent(e.Document, reevaluateExclusion: true, "OnDocumentOpened");
        }

        private static void OnDocumentSaved(object? sender, DocumentSavedEventArgs e)
        {
            if (e.Status != RevitAPIEventStatus.Succeeded) return;
            RefreshFromEvent(e.Document, reevaluateExclusion: false, "OnDocumentSaved");
        }

        private static void OnDocumentSavedAs(object? sender, DocumentSavedAsEventArgs e)
        {
            if (e.Status != RevitAPIEventStatus.Succeeded) return;

            // New path, and possibly a new role (for example saved as central),
            // so classify it again. The old path key is pruned on the next pass.
            RefreshFromEvent(e.Document, reevaluateExclusion: true, "OnDocumentSavedAs");
        }

        private static void OnDocumentSynchronizedWithCentral(object? sender, DocumentSynchronizedWithCentralEventArgs e)
        {
            if (e.Status != RevitAPIEventStatus.Succeeded) return;
            RefreshFromEvent(e.Document, reevaluateExclusion: false, "OnDocumentSynchronizedWithCentral");
        }

        private static void OnDocumentClosing(object? sender, DocumentClosingEventArgs e)
        {
            try
            {
                Autodesk.Revit.DB.Document? doc = e.Document;
                if (doc == null || !doc.IsValidObject) return;

                string path = doc.PathName ?? "";
                if (!string.IsNullOrWhiteSpace(path))
                    Tracked.Remove(path);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("AutoSaveService.OnDocumentClosing", ex);
            }
        }

        private static void RefreshFromEvent(Autodesk.Revit.DB.Document? doc, bool reevaluateExclusion, string context)
        {
            try
            {
                if (doc == null || !doc.IsValidObject) return;

                string path = doc.PathName ?? "";
                if (string.IsNullOrWhiteSpace(path)) return;

                DateTime now = DateTime.UtcNow;

                if (reevaluateExclusion || !Tracked.TryGetValue(path, out TrackedDocument? tracked))
                {
                    Tracked[path] = CreateTracked(doc, now);
                    return;
                }

                tracked.MarkSavedState(now);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"AutoSaveService.{context}", ex);
            }
        }

        // ---------------------------------------------------------------
        // Classification
        // ---------------------------------------------------------------

        private static TrackedDocument CreateTracked(Autodesk.Revit.DB.Document doc, DateTime now)
        {
            string? reason;
            bool isQuietExclusion = false;

            try
            {
                reason = GetPermanentExclusionReason(doc);
                isQuietExclusion = doc.IsFamilyDocument || doc.IsLinked;
            }
            catch (Exception ex)
            {
                // Fail safe: if a document cannot be classified, never save it.
                reason = "could not be classified";
                AppLogger.LogError($"Autosave: classifying '{SafeTitle(doc)}' failed", ex);
            }

            if (reason != null && !isQuietExclusion)
            {
                AppLogger.LogInfo($"Autosave: '{SafeTitle(doc)}' excluded, {reason}.");
            }

            return new TrackedDocument(now, reason);
        }

        /// <summary>
        /// Conditions that hold for the whole time the document stays open
        /// under its current path.
        /// </summary>
        private static string? GetPermanentExclusionReason(Autodesk.Revit.DB.Document doc)
        {
            if (doc.IsFamilyDocument) return "family document";
            if (doc.IsLinked) return "linked document";
            if (doc.IsReadOnlyFile) return "opened from a read only file";

            if (doc.IsModelInCloud && !doc.IsWorkshared && !SaveNonWorksharedCloudModels)
                return "non workshared cloud model, every save would publish a new version";

            if (IsCentralOpenedDirectly(doc)) return "central file opened directly";

            return null;
        }

        /// <summary>
        /// Conditions that may clear on their own. The document is retried
        /// after RetryDelay.
        /// </summary>
        private static string? GetTransientSkipReason(Autodesk.Revit.DB.Document doc)
        {
            if (doc.IsReadOnly) return "the document is temporarily read only";
            if (doc.IsModifiable) return "a transaction is open";
            return null;
        }

        /// <summary>
        /// File based centrals only. GetWorksharingCentralModelPath is used
        /// instead of WorksharingCentralGUID, which only works for cloud models.
        /// Limitation: a central opened through a different path spelling of
        /// the same location (mapped drive versus UNC) is not detected.
        /// </summary>
        private static bool IsCentralOpenedDirectly(Autodesk.Revit.DB.Document doc)
        {
            if (!doc.IsWorkshared || doc.IsModelInCloud) return false;

            Autodesk.Revit.DB.ModelPath? central = doc.GetWorksharingCentralModelPath();
            if (central == null || central.Empty) return false;

            string centralPath = Autodesk.Revit.DB.ModelPathUtils.ConvertModelPathToUserVisiblePath(central);
            return PathsEqual(centralPath, doc.PathName);
        }

        private static bool PathsEqual(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

            string left = a!;
            string right = b!;

            try
            {
                left = System.IO.Path.GetFullPath(left);
                right = System.IO.Path.GetFullPath(right);
            }
            catch
            {
                // Non file system paths (for example RSN://) are compared as is.
            }

            return string.Equals(
                left.TrimEnd('\\', '/'),
                right.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeTitle(Autodesk.Revit.DB.Document doc)
        {
            try
            {
                return doc.Title ?? "(untitled)";
            }
            catch
            {
                return "(unknown)";
            }
        }

        // ---------------------------------------------------------------
        // Tracking state
        // ---------------------------------------------------------------

        private sealed class TrackedDocument
        {
            public TrackedDocument(DateTime savedStateUtc, string? exclusionReason)
            {
                LastSavedStateUtc = savedStateUtc;
                ExclusionReason = exclusionReason;
            }

            public DateTime LastSavedStateUtc { get; private set; }
            public DateTime NextAttemptUtc { get; private set; } = DateTime.MinValue;
            public int ConsecutiveDeferrals { get; private set; }
            public string? ExclusionReason { get; }

            public bool IsExcluded => ExclusionReason != null;

            public bool IsDue(DateTime nowUtc, TimeSpan interval)
            {
                return !IsExcluded
                    && nowUtc >= NextAttemptUtc
                    && nowUtc - LastSavedStateUtc >= interval;
            }

            public void MarkSavedState(DateTime nowUtc)
            {
                LastSavedStateUtc = nowUtc;
                NextAttemptUtc = DateTime.MinValue;
                ConsecutiveDeferrals = 0;
            }

            public void DeferRetry(DateTime nextAttemptUtc)
            {
                NextAttemptUtc = nextAttemptUtc;
                ConsecutiveDeferrals++;
            }
        }
    }
}