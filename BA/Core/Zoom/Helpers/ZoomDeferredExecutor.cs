using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace BA.Zoom.Helpers
{
    internal static class ZoomDeferredExecutor
    {
        private enum Stage
        {
            BouncingToSheet,
            BouncingBackToTarget,
            ReapplyingZoom
        }

        public static void RunAfterViewIsOpen(UIApplication uiApp, View view, Action onEachTick, Action onFinished, int ticks = 10)
        {
            if (uiApp == null) throw new ArgumentNullException(nameof(uiApp));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (onEachTick == null) throw new ArgumentNullException(nameof(onEachTick));
            if (onFinished == null) throw new ArgumentNullException(nameof(onFinished));
            if (ticks < 1) ticks = 1;

            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = view.Document;

            View hostingSheet = TryFindHostingSheet(doc, view);

            bool viewIsCurrentlyActive = doc.ActiveView != null && doc.ActiveView.Id == view.Id;

            if (hostingSheet != null && viewIsCurrentlyActive)
            {
                RunStagedBounceThenZoom(uiApp, uiDoc, hostingSheet, view, onEachTick, onFinished, ticks);
            }
            else
            {
                RunPlainZoom(uiApp, uiDoc, view, onEachTick, onFinished, ticks);
            }
        }

        /// <summary>
        /// Finds the ViewSheet that hosts the given view via a Viewport placement, regardless of whether
        /// that sheet is currently open. Returns null if the view is not placed on any sheet, or if
        /// anything about the lookup fails, so callers can safely fall back to the plain zoom path.
        /// </summary>
        private static View TryFindHostingSheet(Document doc, View targetView)
        {
            try
            {
                Viewport hostingViewport = new FilteredElementCollector(doc)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>()
                    .FirstOrDefault(vp => vp.ViewId == targetView.Id);

                if (hostingViewport == null)
                    return null;

                ElementId sheetId = hostingViewport.SheetId;
                if (sheetId == null || sheetId == ElementId.InvalidElementId)
                    return null;

                return doc.GetElement(sheetId) as View;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The normal path: the target view is not currently activated in place inside a sheet, so a
        /// single RequestViewChange plus reapplying the zoom across several idle ticks is enough.
        /// </summary>
        private static void RunPlainZoom(UIApplication uiApp, UIDocument uiDoc, View view, Action onEachTick, Action onFinished, int ticks)
        {
            uiDoc.RequestViewChange(view);

            int remaining = ticks;
            EventHandler<IdlingEventArgs> handler = null;
            handler = (sender, e) =>
            {
                try
                {
                    onEachTick();
                    remaining--;
                    if (remaining <= 0)
                    {
                        uiApp.Idling -= handler;
                        onFinished();
                    }
                }
                catch
                {
                    uiApp.Idling -= handler;
                    onFinished();
                }
            };
            uiApp.Idling += handler;
        }

        /// <summary>
        /// The activated-in-place path. RequestViewChange appears to no-op when the requested view already
        /// equals doc.ActiveView, which is exactly the state a view activated in place inside a sheet is
        /// in, even though what's on screen is still the constrained, embedded rendering, not a genuine
        /// standalone tab. This forces a real state transition: request the hosting sheet first, wait
        /// across idle ticks until doc.ActiveView actually confirms that, then request the target view a
        /// second time from that different starting point, wait for that to confirm, and only then start
        /// reapplying the zoom across the normal tick count. Each stage times out on its own after
        /// maxWaitTicksPerStage idle ticks and moves on regardless, so a confirmation that never arrives
        /// cannot leave the Idling subscription hanging forever.
        /// </summary>
        private static void RunStagedBounceThenZoom(UIApplication uiApp, UIDocument uiDoc, View hostingSheet, View targetView, Action onEachTick, Action onFinished, int ticks)
        {
            const int maxWaitTicksPerStage = 20;

            uiDoc.RequestViewChange(hostingSheet);

            Stage stage = Stage.BouncingToSheet;
            int stageWaitRemaining = maxWaitTicksPerStage;
            int zoomTicksRemaining = ticks;

            EventHandler<IdlingEventArgs> handler = null;
            handler = (sender, e) =>
            {
                try
                {
                    Document doc = targetView.Document;

                    switch (stage)
                    {
                        case Stage.BouncingToSheet:
                            stageWaitRemaining--;
                            if ((doc.ActiveView != null && doc.ActiveView.Id == hostingSheet.Id) || stageWaitRemaining <= 0)
                            {
                                uiDoc.RequestViewChange(targetView);
                                stage = Stage.BouncingBackToTarget;
                                stageWaitRemaining = maxWaitTicksPerStage;
                            }
                            break;

                        case Stage.BouncingBackToTarget:
                            stageWaitRemaining--;
                            if ((doc.ActiveView != null && doc.ActiveView.Id == targetView.Id) || stageWaitRemaining <= 0)
                            {
                                stage = Stage.ReapplyingZoom;
                            }
                            break;

                        case Stage.ReapplyingZoom:
                            onEachTick();
                            zoomTicksRemaining--;
                            if (zoomTicksRemaining <= 0)
                            {
                                uiApp.Idling -= handler;
                                onFinished();
                            }
                            break;
                    }
                }
                catch
                {
                    uiApp.Idling -= handler;
                    onFinished();
                }
            };

            uiApp.Idling += handler;
        }
    }
}