// File: BA_Tools/VisibilityDiagnostics/Services/VisibilityWarningWatcher.cs
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BA.App.Overhead;
using BA.BAApplication;// <- ASSUMPTION, unchanged: only inferred from adjacency in
                       //    BaApplication.cs's usings. Fix this one line if AppLogger
                       //    doesn't resolve.
using BA.VisibilityDiagnostics.Commands;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.VisibilityDiagnostics.Services
{
    /// <summary>
    /// Watches for Revit's "None of the created elements are visible in [view]..."
    /// warning - FailureDefinitionId b1c8f862-00fc-4046-b92c-6ca64d53384f, confirmed
    /// from your own log (fires only through FailuresProcessing, not
    /// Document.GetWarnings()) - and offers three choices: open the Visibility
    /// Diagnostic Tool (runs diagnosis immediately, no extra click), delete the
    /// invisible element(s) on the spot, or do nothing.
    ///
    /// Always prompts on every occurrence (per your earlier choice) - no per-session or
    /// per-commit suppression yet.
    ///
    /// The TaskDialog prompt and the delete action both run synchronously inside
    /// FailuresProcessing - the normal, supported place for interactive failure
    /// resolution, and exactly where FailuresAccessor.DeleteElements is meant to be
    /// called (it resolves the failure by removing the elements it refers to, within
    /// the transaction currently being processed - no separate Transaction needed).
    /// Opening the diagnostic window is different: it needs a fully-committed
    /// document, so that path alone is deferred to the next Idling tick, same as
    /// before.
    /// </summary>
    public static class VisibilityWarningWatcher
    {
        private static readonly Guid ElementsNotVisibleFailureDefinitionGuid =
            new Guid("b1c8f862-00fc-4046-b92c-6ca64d53384f");

        private static bool _registered;
        private static UIApplication? _uiApplication;
        private static ElementId? _pendingElementId;

        public static void Register(UIApplication uiApplication)
        {
            if (_registered || uiApplication is null)
            {
                return;
            }

            _uiApplication = uiApplication;
            uiApplication.Application.FailuresProcessing += OnFailuresProcessing;
            _registered = true;
        }

        public static void Unregister(UIApplication uiApplication)
        {
            if (!_registered || uiApplication is null)
            {
                return;
            }

            uiApplication.Application.FailuresProcessing -= OnFailuresProcessing;

            if (_pendingElementId is not null)
            {
                uiApplication.Idling -= OnIdlingOpenTool;
                _pendingElementId = null;
            }

            _registered = false;
            _uiApplication = null;
        }

        private static void OnFailuresProcessing(object sender, FailuresProcessingEventArgs e)
        {
            try
            {
                FailuresAccessor accessor = e.GetFailuresAccessor();
                var matchedElementIds = new List<ElementId>();

                foreach (FailureMessageAccessor message in accessor.GetFailureMessages())
                {
                    if (message.GetFailureDefinitionId().Guid != ElementsNotVisibleFailureDefinitionGuid)
                    {
                        continue;
                    }

                    foreach (ElementId id in message.GetFailingElementIds())
                    {
                        if (!matchedElementIds.Contains(id))
                        {
                            matchedElementIds.Add(id);
                        }
                    }
                }

                if (matchedElementIds.Count == 0)
                {
                    return;
                }

                PromptAndAct(accessor, matchedElementIds);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("VisibilityWarningWatcher.OnFailuresProcessing", ex);
            }
        }

        private static void PromptAndAct(FailuresAccessor accessor, IReadOnlyList<ElementId> elementIds)
        {
            string mainInstruction = elementIds.Count == 1
                ? "One of the elements you just created is not visible in the active view."
                : $"{elementIds.Count} of the elements you just created are not visible in the active view.";

            var dialog = new TaskDialog("Elements Not Visible")
            {
                MainInstruction = mainInstruction,
                MainContent = "What would you like to do?",
                CommonButtons = TaskDialogCommonButtons.None,
                AllowCancellation = true,
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open Visibility Diagnostic Tool",
                "Diagnose why this element isn't visible - runs the full checklist immediately.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Delete the created element(s)",
                elementIds.Count == 1
                    ? "Remove the element you just placed."
                    : $"Remove all {elementIds.Count} elements you just placed.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Do nothing",
                "Leave the element(s) as they are and dismiss this.");

            TaskDialogResult result = dialog.Show();

            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    ScheduleOpenTool(elementIds[0]);
                    break;

                case TaskDialogResult.CommandLink2:
                    DeleteElements(accessor, elementIds);
                    break;

                default:
                    // CommandLink3, Cancel, or Esc - do nothing.
                    break;
            }
        }

        private static void ScheduleOpenTool(ElementId elementId)
        {
            if (_uiApplication is null)
            {
                return;
            }

            // Defer actually opening the window/running diagnosis until the transaction
            // that's currently being resolved has fully committed - FailuresProcessing
            // fires before commit finishes, and Idling only fires between transactions,
            // so this guarantees the diagnostic checks run against fully-settled document
            // state. Same one-shot subscribe/unsubscribe pattern BaApplication.OnFirstIdling
            // already uses elsewhere in this codebase.
            _pendingElementId = elementId;
            _uiApplication.Idling += OnIdlingOpenTool;
        }

        private static void DeleteElements(FailuresAccessor accessor, IReadOnlyList<ElementId> elementIds)
        {
            try
            {
                // Resolves the failure by deleting the offending elements directly
                // within the transaction currently being processed - no separate
                // Transaction needed, and no separate call to dismiss the warning:
                // the elements it refers to no longer exist once this returns.
                accessor.DeleteElements(elementIds.ToList()); // <- UNCONFIRMED signature, needs your compiler
                AppLogger.LogInfo(
                    $"[VisibilityWarningWatcher] Deleted {elementIds.Count} element(s) not visible in the active view: " +
                    $"[{string.Join(", ", elementIds.Select(id => id.Value))}].");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("VisibilityWarningWatcher.DeleteElements", ex);
            }
        }

        private static void OnIdlingOpenTool(object sender, IdlingEventArgs e)
        {
            if (sender is not UIApplication uiApp)
            {
                return;
            }

            uiApp.Idling -= OnIdlingOpenTool;

            if (_pendingElementId is not { } elementId)
            {
                return;
            }

            _pendingElementId = null;

            try
            {
                VisibilityDiagnosticCommand.ShowForElement(uiApp, elementId);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("VisibilityWarningWatcher.OnIdlingOpenTool", ex);
            }
        }
    }
}