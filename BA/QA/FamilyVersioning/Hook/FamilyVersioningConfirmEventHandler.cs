using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.QA.FamilyVersioning.Data;
using BA.QA.FamilyVersioning.Models;
using BA.QA.FamilyVersioning.ViewModels;
using BA.QA.FamilyVersioning.Views;
using BA.UI.Helpers;

namespace BA.QA.FamilyVersioning.Hook
{
    /// <summary>
    /// IExternalEventHandler implementation that drains the pending detection queue
    /// and shows the confirm dialog for each entry. Called via ExternalEvent.Raise()
    /// at the end of each DocumentChanged batch that produced detections.
    ///
    /// Why ExternalEvent and not a direct ShowDialog call from DocumentChanged:
    /// DocumentChanged fires synchronously during Revit's document modification
    /// processing. Showing a WPF modal from inside that synchronous context blocks
    /// the Revit API thread while the document change transaction is still being
    /// finalized, which causes Revit to deadlock or produce undefined behavior.
    /// ExternalEvent defers execution until Revit's next idle/event processing
    /// cycle, at which point the modification is fully committed and it is safe to
    /// show a blocking dialog.
    ///
    /// One entry per Raise call: Execute drains the entire queue in one call rather
    /// than raising once per detection, because Revit coalesces rapid ExternalEvent
    /// raises (if the user loads 3 families in quick succession before the first
    /// Raise is processed, all 3 end up in the queue and Execute shows dialogs for
    /// all 3 sequentially in the same Execute call). This is the correct behavior:
    /// the user sees each detection in order without requiring 3 separate Raise/
    /// Execute cycles.
    /// </summary>
    public sealed class FamilyVersioningConfirmEventHandler : IExternalEventHandler
    {
        private readonly ConcurrentDictionary<string, FamilyVersioningSession> _sessions;

        public FamilyVersioningConfirmEventHandler(
            ConcurrentDictionary<string, FamilyVersioningSession> sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public string GetName() => "BA.FamilyVersioning.ConfirmDetection";

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
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

                while (session.PendingDetections.TryDequeue(out var detection))
                {
                    ProcessDetection(app, session, detection);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("FamilyVersioningConfirmEventHandler.Execute", ex);
            }
        }

        private void ProcessDetection(
            UIApplication app,
            FamilyVersioningSession session,
            PendingDetection detection)
        {
            try
            {
                var familyRepo = new FamilyRepository(session.CatalogFactory);
                var stateRepo = new FamilyBuildingStateRepository(session.CatalogFactory);
                var exceptionRepo = new ExceptionRepository(session.CatalogFactory);
                var auditRepo = new AuditLogRepository(session.CatalogFactory);

                if (exceptionRepo.ExistsActiveException(detection.FamilyId, session.BuildingId))
                {
                    stateRepo.Upsert(
                        detection.FamilyId,
                        session.BuildingId,
                        detection.SuggestedVersion,
                        detection.NewSnapshot.Hash,
                        app.Application.Username,
                        detection.InferredBumpKind,
                        detection.Diff.ToSummaryString());

                    auditRepo.LogEvent(
                        detection.FamilyId,
                        session.BuildingId,
                        AuditEventType.Confirmed,
                        app.Application.Username,
                        detail: "Auto-confirmed: active exception exists.",
                        diffSummary: detection.Diff.ToSummaryString());

                    return;
                }

                var viewModel = new FamilyVersioningConfirmViewModel(detection, session.BuildingName);
                var window = new FamilyVersioningConfirmWindow(viewModel);
                RevitWindowHelper.ShowDialogOwnedByRevit(window, app);

                if (!viewModel.UserConfirmed)
                {
                    auditRepo.LogEvent(
                        detection.FamilyId,
                        session.BuildingId,
                        AuditEventType.Detected,
                        app.Application.Username,
                        detail: "Dialog dismissed without confirmation.",
                        diffSummary: detection.Diff.ToSummaryString());
                    return;
                }

                var finalVersion = viewModel.FinalVersion;
                var finalComment = viewModel.Comment;
                var markedAsException = viewModel.MarkedAsException;

                stateRepo.Upsert(
                    detection.FamilyId,
                    session.BuildingId,
                    finalVersion,
                    detection.NewSnapshot.Hash,
                    app.Application.Username,
                    detection.InferredBumpKind,
                    detection.Diff.ToSummaryString());

                if (markedAsException)
                {
                    exceptionRepo.AddException(
                        detection.FamilyId,
                        session.BuildingId,
                        string.IsNullOrWhiteSpace(finalComment)
                            ? "(No reason provided)"
                            : finalComment,
                        app.Application.Username);

                    auditRepo.LogEvent(
                        detection.FamilyId,
                        session.BuildingId,
                        AuditEventType.ExceptionMarked,
                        app.Application.Username,
                        detail: string.IsNullOrWhiteSpace(finalComment)
                            ? null
                            : finalComment,
                        diffSummary: detection.Diff.ToSummaryString());
                }
                else if (viewModel.Overridden)
                {
                    auditRepo.LogEvent(
                        detection.FamilyId,
                        session.BuildingId,
                        AuditEventType.Overridden,
                        app.Application.Username,
                        detail: string.IsNullOrWhiteSpace(finalComment)
                            ? $"Version overridden from '{detection.SuggestedVersion}' to '{finalVersion}'."
                            : $"Version overridden from '{detection.SuggestedVersion}' to '{finalVersion}'. {finalComment}",
                        diffSummary: detection.Diff.ToSummaryString());
                }
                else
                {
                    auditRepo.LogEvent(
                        detection.FamilyId,
                        session.BuildingId,
                        AuditEventType.Confirmed,
                        app.Application.Username,
                        detail: string.IsNullOrWhiteSpace(finalComment)
                            ? null
                            : finalComment,
                        diffSummary: detection.Diff.ToSummaryString());
                }

                if (viewModel.SetAsCanonical)
                {
                    familyRepo.UpdateCanonicalState(
                        detection.FamilyId,
                        finalVersion,
                        detection.NewSnapshot.Hash,
                        sourcePath: null);

                    auditRepo.LogEvent(
                        detection.FamilyId,
                        session.BuildingId,
                        AuditEventType.Confirmed,
                        app.Application.Username,
                        detail: $"Set as canonical version: {finalVersion}.",
                        diffSummary: null);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError(
                    $"FamilyVersioningConfirmEventHandler.ProcessDetection " +
                    $"(Family={detection.FamilyName}, Building={session.BuildingName})", ex);
            }
        }
    }
}
