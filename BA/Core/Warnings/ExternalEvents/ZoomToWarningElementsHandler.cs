// FILE: BA_Tools/Warnings/ExternalEvents/ZoomToWarningElementsHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Zoom.Helpers;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.Warnings.ExternalEvents
{
    // Follows the exact zoom pattern from BA.Zoom.Commands.Cmd_ZoomToSelectedElement:
    // requires a plan-like active view, flattens to XY at Z=0, uses
    // ZoomGeometryHelper.NormalizeAndBufferRectangle with the same 600 buffer,
    // and UIView.ZoomAndCenterRectangle rather than ShowElements. Generalized here
    // to union the bounding box across all of a warning's failing + additional
    // elements instead of a single selected element.
    //
    // ASSUMPTION FLAGGED: I only know ZoomRevitHelper.IsPlanLike(View) and
    // ZoomGeometryHelper.NormalizeAndBufferRectangle(ref XYZ, ref XYZ, double)
    // from their call sites in your pasted file, not their implementations.
    // If NormalizeAndBufferRectangle does anything unit-sensitive beyond what's
    // visible from the call, tell me.
    public sealed class ZoomToWarningElementsHandler : IExternalEventHandler
    {
        private static ExternalEvent _event;
        private ICollection<ElementId> _targetIds;
        private Action<bool> _onCompleted;

        public static ZoomToWarningElementsHandler Instance { get; } = new ZoomToWarningElementsHandler();

        private ZoomToWarningElementsHandler() { }

        public void RequestZoom(ICollection<ElementId> elementIds, Action<bool> onCompleted = null)
        {
            if (elementIds == null || elementIds.Count == 0)
            {
                onCompleted?.Invoke(false);
                return;
            }

            _targetIds = elementIds;
            _onCompleted = onCompleted;

            _event ??= ExternalEvent.Create(this);
            _event.Raise();
        }

        public void Execute(UIApplication app)
        {
            bool success = false;
            try
            {
                UIDocument uiDoc = app.ActiveUIDocument;
                if (uiDoc == null)
                {
                    TaskDialog.Show("Zoom to Elements", "No active document.");
                    return;
                }

                Document doc = uiDoc.Document;
                View view = doc.ActiveView;

                if (!ZoomRevitHelper.IsPlanLike(view))
                {
                    TaskDialog.Show("Zoom to Elements", "Open a floor plan, ceiling plan, engineering plan, or area plan view.");
                    return;
                }

                List<ElementId> liveIds = _targetIds.Where(id => doc.GetElement(id) != null).ToList();

                if (liveIds.Count == 0)
                {
                    TaskDialog.Show("Zoom to Elements", "None of the failing elements still exist in the model. This warning is stale, refresh the dashboard.");
                    return;
                }

                double? minX = null, minY = null, maxX = null, maxY = null;
                int visibleCount = 0;

                foreach (ElementId id in liveIds)
                {
                    Element el = doc.GetElement(id);
                    BoundingBoxXYZ bb = el.get_BoundingBox(view);
                    if (bb == null) continue;

                    visibleCount++;

                    double bMinX = Math.Min(bb.Min.X, bb.Max.X);
                    double bMinY = Math.Min(bb.Min.Y, bb.Max.Y);
                    double bMaxX = Math.Max(bb.Min.X, bb.Max.X);
                    double bMaxY = Math.Max(bb.Min.Y, bb.Max.Y);

                    minX = minX.HasValue ? Math.Min(minX.Value, bMinX) : bMinX;
                    minY = minY.HasValue ? Math.Min(minY.Value, bMinY) : bMinY;
                    maxX = maxX.HasValue ? Math.Max(maxX.Value, bMaxX) : bMaxX;
                    maxY = maxY.HasValue ? Math.Max(maxY.Value, bMaxY) : bMaxY;
                }

                if (visibleCount == 0)
                {
                    TaskDialog.Show("Zoom to Elements", "None of the failing elements are visible in the active view. Switch to a view where they're shown, then retry.");
                    return;
                }

                XYZ minXY = new XYZ(minX.Value, minY.Value, 0);
                XYZ maxXY = new XYZ(maxX.Value, maxY.Value, 0);
                ZoomGeometryHelper.NormalizeAndBufferRectangle(ref minXY, ref maxXY, 600);

                UIView uiView = uiDoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id);
                if (uiView == null)
                {
                    TaskDialog.Show("Zoom to Elements", "UIView for the active view not found.");
                    return;
                }

                uiView.ZoomAndCenterRectangle(minXY, maxXY);
                uiDoc.Selection.SetElementIds(liveIds);
                success = true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ZoomToWarningElementsHandler.Execute", ex);
                TaskDialog.Show("Zoom to Elements", "An error occurred while zooming to the failing elements. See log for details.");
            }
            finally
            {
                _onCompleted?.Invoke(success);
                _onCompleted = null;
                _targetIds = null;
            }
        }

        public string GetName() => "BA Zoom To Warning Elements";
    }
}