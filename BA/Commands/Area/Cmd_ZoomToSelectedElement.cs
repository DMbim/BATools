using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BATools.Zoom.Helpers;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BATools.Zoom.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    public class Cmd_ZoomToSelectedElement : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            var view = doc.ActiveView;

            if (!ZoomRevitHelper.IsPlanLike(view))
            {
                TaskDialog.Show("Zoom to Element", "Open a floor plan, ceiling plan, engineering plan, or area plan view.");
                return Result.Failed;
            }

            var ids = uiDoc.Selection.GetElementIds();
            if (!ids.Any())
            {
                TaskDialog.Show("Zoom to Element", "No elements are selected.");
                return Result.Cancelled;
            }

            var el = doc.GetElement(ids.First());
            if (el == null)
            {
                TaskDialog.Show("Zoom to Element", "Failed to retrieve the selected element.");
                return Result.Failed;
            }

            var bb = el.get_BoundingBox(view);
            if (bb == null)
            {
                TaskDialog.Show("Zoom to Element", "The selected element has no bounding box in the active view.");
                return Result.Failed;
            }

            XYZ minXY = new XYZ(Math.Min(bb.Min.X, bb.Max.X), Math.Min(bb.Min.Y, bb.Max.Y), 0);
            XYZ maxXY = new XYZ(Math.Max(bb.Min.X, bb.Max.X), Math.Max(bb.Min.Y, bb.Max.Y), 0);
            ZoomGeometryHelper.NormalizeAndBufferRectangle(ref minXY, ref maxXY, 600);

            var uiView = uiDoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id);
            if (uiView == null)
            {
                TaskDialog.Show("Zoom to Element", "UIView for the active view not found.");
                return Result.Failed;
            }

            uiView.ZoomAndCenterRectangle(minXY, maxXY);
            TaskDialog.Show("Zoom to Element", $"Zoomed to element {el.Id.Value}.");
            return Result.Succeeded;
        }
    }
}