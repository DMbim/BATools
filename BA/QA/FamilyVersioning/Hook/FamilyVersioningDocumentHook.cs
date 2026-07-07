using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.QA.FamilyVersioning.Data;
using BA.QA.FamilyVersioning.Engine;
using BA.QA.FamilyVersioning.Models;

namespace BA.QA.FamilyVersioning.Hook
{
    /// <summary>
    /// Registers and handles DocumentOpened, DocumentChanged, and DocumentClosed
    /// events for the Family Versioning detection system. Intended to be constructed
    /// once in BaApplication.OnFirstIdling and kept alive for the session.
    ///
    /// Threading model:
    /// - DocumentOpened and DocumentChanged fire on the Revit API thread.
    /// - Hash extraction and catalog reads happen inline on that thread (fast,
    ///   read-only, no transaction).
    /// - The WPF confirm dialog is shown via ExternalEvent so Revit can finish
    ///   processing the document change before the modal appears.
    /// - Catalog writes happen inside the ExternalEvent handler after user confirms,
    ///   never inside DocumentChanged itself.
    ///
    /// Startup timing gap: DocumentOpened for the startup/session-restore document
    /// fires before OnFirstIdling, meaning before this hook is registered. Call
    /// SeedOpenDocuments() immediately after Register() to retroactively create
    /// sessions for already-open documents.
    /// </summary>
    public sealed class FamilyVersioningDocumentHook : IDisposable
    {
        private readonly Autodesk.Revit.ApplicationServices.ControlledApplication _controlledApp;
        private readonly UIApplication _uiApp;
        private readonly ExternalEvent _confirmExternalEvent;
        private readonly FamilyVersioningConfirmEventHandler _confirmEventHandler;

        private readonly ConcurrentDictionary<string, FamilyVersioningSession> _sessions = new(
            StringComparer.OrdinalIgnoreCase);

        private bool _disposed;

        public FamilyVersioningDocumentHook(
            Autodesk.Revit.ApplicationServices.ControlledApplication controlledApp,
            UIApplication uiApp)
        {
            _controlledApp = controlledApp ?? throw new ArgumentNullException(nameof(controlledApp));
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));

            _confirmEventHandler = new FamilyVersioningConfirmEventHandler(_sessions);
            _confirmExternalEvent = ExternalEvent.Create(_confirmEventHandler);
        }

        public void Register()
        {
            _controlledApp.DocumentOpened += OnDocumentOpened;
            _controlledApp.DocumentChanged += OnDocumentChanged;
            _controlledApp.DocumentClosed += OnDocumentClosed;
        }

        public void Unregister()
        {
            _controlledApp.DocumentOpened -= OnDocumentOpened;
            _controlledApp.DocumentChanged -= OnDocumentChanged;
            _controlledApp.DocumentClosed -= OnDocumentClosed;
        }

        /// <summary>
        /// Scans all currently open documents and creates sessions for any that have
        /// a valid catalog configured but were opened before this hook was registered.
        /// Must be called immediately after Register() to handle documents already
        /// open at startup (last session restore, startup document). Safe to call
        /// multiple times, existing sessions are not overwritten.
        /// </summary>
        public void SeedOpenDocuments()
        {
            try
            {
                foreach (Document doc in _uiApp.Application.Documents)
                {
                    if (doc == null || doc.IsFamilyDocument)
                    {
                        continue;
                    }

                    var modelPath = doc.PathName;
                    if (string.IsNullOrWhiteSpace(modelPath) ||
                        _sessions.ContainsKey(modelPath))
                    {
                        continue;
                    }

                    TryCreateSession(modelPath);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("FamilyVersioningDocumentHook.SeedOpenDocuments", ex);
            }
        }

        private void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
        {
            try
            {
                var doc = e.Document;
                if (doc == null || doc.IsFamilyDocument)
                {
                    return;
                }

                var modelPath = doc.PathName;
                if (string.IsNullOrWhiteSpace(modelPath))
                {
                    return;
                }

                TryCreateSession(modelPath);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("FamilyVersioningDocumentHook.OnDocumentOpened", ex);
            }
        }

        /// <summary>
        /// Core session creation logic, shared between OnDocumentOpened and
        /// SeedOpenDocuments. Safe to call for a path that already has a session
        /// (exits early via ContainsKey check), so SeedOpenDocuments can call it
        /// without worrying about overwriting a session created by OnDocumentOpened
        /// if both fire for the same document.
        /// </summary>
        private void TryCreateSession(string modelPath)
        {
            if (_sessions.ContainsKey(modelPath))
            {
                return;
            }

            var settings = TryLoadSettings(modelPath);
            if (settings == null ||
                string.IsNullOrWhiteSpace(settings.CatalogDatabasePath))
            {
                return;
            }

            var factory = new CatalogConnectionFactory(settings.CatalogDatabasePath);
            var buildingRepo = new BuildingRepository(factory);
            var building = buildingRepo.FindByCentralModelPath(modelPath);

            if (building == null)
            {
                AppLogger.LogInfo(
                    $"[FamilyVersioning] Document '{modelPath}' has a catalog configured " +
                    $"at '{settings.CatalogDatabasePath}' but no matching building row was found. " +
                    "Add this model in the Family Versioning Setup window to enable detection.");
                return;
            }

            if (!building.Enabled)
            {
                AppLogger.LogInfo(
                    $"[FamilyVersioning] Building '{building.BuildingName}' is disabled. " +
                    "Detection skipped for this session.");
                return;
            }

            var categoryRepo = new TrackedCategoryRepository(factory);
            var trackedCategoryIds = categoryRepo.GetEnabledBuiltInCategoryIds();

            if (trackedCategoryIds.Count == 0)
            {
                AppLogger.LogInfo(
                    $"[FamilyVersioning] No tracked categories configured for building " +
                    $"'{building.BuildingName}'. All family categories will be detected until " +
                    "categories are configured in the Family Versioning Setup window.");
            }

            var session = new FamilyVersioningSession(
                modelPath, building.BuildingId, building.BuildingName, factory, trackedCategoryIds);

            _sessions[modelPath] = session;

            AppLogger.LogInfo(
                $"[FamilyVersioning] Session started for '{building.BuildingName}' " +
                $"(BuildingId={building.BuildingId}), catalog at '{settings.CatalogDatabasePath}'.");
        }

        private void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            try
            {
                var doc = e.GetDocument();
                if (doc == null || doc.IsFamilyDocument)
                {
                    return;
                }

                var modelPath = doc.PathName;
                if (string.IsNullOrWhiteSpace(modelPath) ||
                    !_sessions.TryGetValue(modelPath, out var session))
                {
                    return;
                }

                var addedAndModified = e.GetAddedElementIds()
                    .Concat(e.GetModifiedElementIds())
                    .Distinct();

                var familyIds = new List<ElementId>();
                foreach (var id in addedAndModified)
                {
                    if (doc.GetElement(id) is Family)
                    {
                        familyIds.Add(id);
                    }
                }

                if (familyIds.Count == 0)
                {
                    return;
                }

                var familyRepo = new FamilyRepository(session.CatalogFactory);
                var stateRepo = new FamilyBuildingStateRepository(session.CatalogFactory);

                foreach (var familyId in familyIds)
                {
                    ProcessFamilyChange(doc, familyId, session, familyRepo, stateRepo);
                }

                if (!session.PendingDetections.IsEmpty)
                {
                    _confirmExternalEvent.Raise();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("FamilyVersioningDocumentHook.OnDocumentChanged", ex);
            }
        }

        private void ProcessFamilyChange(
            Document doc,
            ElementId familyId,
            FamilyVersioningSession session,
            FamilyRepository familyRepo,
            FamilyBuildingStateRepository stateRepo)
        {
            try
            {
                var newSnapshot = FamilyHashEngine.ExtractSnapshot(doc, familyId);
                if (newSnapshot == null)
                {
                    return;
                }

                // Category filter: if tracked categories are configured, only process
                // families whose category's BuiltInCategory integer value is in the
                // enabled set. If no categories are configured (empty set), all families
                // pass through, which is the safe first-run behavior.
                if (session.TrackedCategoryIds.Count > 0)
                {
                    var family = doc.GetElement(familyId) as Autodesk.Revit.DB.Family;
                    if (family != null)
                    {
                        var categoryId = family.FamilyCategory?.Id?.Value ?? long.MinValue;
                        if (!session.TrackedCategoryIds.Contains((int)categoryId))
                        {
                            return;
                        }
                    }
                }

                var trackedFamily = familyRepo.GetOrCreate(newSnapshot.FamilyName, newSnapshot.CategoryName);
                var existingState = stateRepo.Get(trackedFamily.FamilyId, session.BuildingId);

                var hashUnchanged = existingState != null &&
                    string.Equals(existingState.LoadedHash, newSnapshot.Hash, StringComparison.OrdinalIgnoreCase);

                // Pass null as previousSnapshot to treat every load as a fresh observation
                // for diff display purposes. This means the diff always shows current
                // types/params as "added" rather than a true delta, which is acceptable
                // since we're always triggering regardless of hash change.
                var diff = FamilyHashEngine.Diff(null, newSnapshot);
                var inferredBump = hashUnchanged ? FamilyBumpKind.Unknown : FamilyHashEngine.InferBumpKind(diff);
                var currentVersion = existingState?.LoadedVersion ?? trackedFamily.CanonicalVersion;

                // If hash is unchanged, suggest keeping the current version rather than
                // bumping it, the dialog will show "No structural changes detected" and
                // the user can confirm or dismiss without inflating the version number.
                var suggestedVersion = hashUnchanged
                    ? currentVersion
                    : FamilyHashEngine.BumpVersion(currentVersion, inferredBump);

                var detection = new PendingDetection(
                    familyId,
                    newSnapshot.FamilyName,
                    newSnapshot.CategoryName,
                    newSnapshot,
                    null,
                    diff,
                    inferredBump,
                    suggestedVersion,
                    currentVersion,
                    trackedFamily.FamilyId);

                session.PendingDetections.Enqueue(detection);
            }
            catch (Exception ex)
            {
                AppLogger.LogError(
                    $"FamilyVersioningDocumentHook.ProcessFamilyChange (FamilyId={familyId.Value})", ex);
            }
        }

        private void OnDocumentClosed(object sender, DocumentClosedEventArgs e)
        {
            try
            {
                var pathToRemove = _sessions.Keys
                    .FirstOrDefault(path => !IsDocumentStillOpen(path));

                if (pathToRemove != null)
                {
                    _sessions.TryRemove(pathToRemove, out _);
                    AppLogger.LogInfo($"[FamilyVersioning] Session ended for '{pathToRemove}'.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("FamilyVersioningDocumentHook.OnDocumentClosed", ex);
            }
        }

        private bool IsDocumentStillOpen(string path)
        {
            try
            {
                foreach (Document doc in _uiApp.Application.Documents)
                {
                    if (string.Equals(doc.PathName, path, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static FamilyVersioningSettings? TryLoadSettings(string modelPath)
        {
            try
            {
                return FamilyVersioningSettingsStore.Load(modelPath);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("FamilyVersioningDocumentHook.TryLoadSettings", ex);
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Unregister();
            _confirmExternalEvent?.Dispose();
            _disposed = true;
        }
    }
}
