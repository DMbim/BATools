// BA/Markup/Commands/MarkupNotificationHandler.cs
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Markup.Models;
using BA.Markup.Services;
using BA.Markup.Settings;
using BA.Markup.ViewModels;
using BA.Markup.Views;

namespace BA.Markup.Commands
{
    /// <summary>
    /// Entry point called from BaApplication.OnDocumentSynchronizedWithCentral, after
    /// SynchronizeGuard.IsSynchronizing is cleared. Records sync participation, scans for
    /// markups assigned to the current user, diffs against the user's local baseline, and
    /// shows a modal notification window if there is anything unacknowledged.
    ///
    /// Requires a live UIApplication, not just Document, because the notification window's
    /// Go To View action needs a UIDocument to change the active view; Document alone has no
    /// such capability. UIApplication is captured once in BaApplication.OnFirstIdling and
    /// passed down here, DocumentSynchronizedWithCentral itself only exposes the DB
    /// Application, not UIApplication.
    ///
    /// Runs synchronously on the Revit API thread, so the notification window is shown via
    /// ShowDialog(), not as a modeless window. This mirrors RevisionManagerWindow/MarkupWindow's
    /// existing pattern: staying on the API thread means Go To View / Mark WIP / Mark Solved
    /// can run transactions directly, no ExternalEvent/dispatcher bridge needed.
    ///
    /// Window pops up if ANY scanned item has BA_Tls_WIP == false, regardless of the
    /// baseline IsNew flag. IsNew is still computed and carried per item for the grid to
    /// badge as "new since last sync", it just does not gate whether the window appears.
    /// </summary>
    public static class MarkupNotificationHandler
    {
        public static void OnSyncCompleted(Document doc, UIApplication uiApp, IntPtr ownerHandle)
        {
            if (doc == null)
                return;

            try
            {
                string username = doc.Application?.Username;
                if (string.IsNullOrWhiteSpace(username))
                    return;

                MarkupUserRegistryService.RecordParticipation(doc, username);

                IReadOnlyList<MarkupNotificationItem> raw = MarkupScanService.ScanForUser(doc, username);
                if (raw.Count == 0)
                    return;

                IReadOnlyList<MarkupNotificationItem> diffed =
                    MarkupBaselineService.DiffAndUpdateBaseline(doc, username, raw);

                if (diffed.Count == 0 || !ShouldShowWindow(diffed))
                    return;

                // UIDocument is only resolvable if the synced document is the currently
                // active one. If a user has multiple documents open and syncs a
                // non-active one, ActiveUIDocument.Document != doc, in which case Go To
                // View is disabled inside the ViewModel rather than failing hard, the
                // notification list itself is still shown and useful without it.
                UIDocument uiDoc = null;
                if (uiApp?.ActiveUIDocument?.Document == doc)
                    uiDoc = uiApp.ActiveUIDocument;

                ShowNotificationWindow(doc, uiDoc, diffed, ownerHandle);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MarkupNotificationHandler.OnSyncCompleted", ex);
            }
        }

        private static bool ShouldShowWindow(IReadOnlyList<MarkupNotificationItem> items)
        {
            foreach (var item in items)
            {
                if (!item.Wip)
                    return true;
            }
            return false;
        }

        private static void ShowNotificationWindow(
            Document doc,
            UIDocument uiDoc,
            IReadOnlyList<MarkupNotificationItem> items,
            IntPtr ownerHandle)
        {
            var viewModel = new MarkupNotificationViewModel(doc, uiDoc, items);
            var window = new MarkupNotificationWindow(viewModel);

            if (ownerHandle != IntPtr.Zero)
                new System.Windows.Interop.WindowInteropHelper(window).Owner = ownerHandle;

            window.ShowDialog();
        }
    }
}