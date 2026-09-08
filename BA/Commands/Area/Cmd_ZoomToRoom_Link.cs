using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using BA.Zoom.Helpers;
using BA.Zoom.Services;
using BA.Zoom.Settings;
using BA.Zoom.Views;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.Zoom.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class Cmd_ZoomToRoom_Link : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document host = uiDoc?.Document;

            if (host == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            try
            {
                var settings = ZoomToRoomSettings.Load();

                var linkInst = new FilteredElementCollector(host)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .FirstOrDefault(li => string.Equals(
                        li.Name, settings.SelectedRevitLinkName, StringComparison.OrdinalIgnoreCase));

                if (linkInst == null)
                {
                    TaskDialog.Show("Zoom to Room",
                        "No Revit link configured. Use the Zoom to Room Settings button to select one.");
                    return Result.Cancelled;
                }

                var linkedDoc = linkInst.GetLinkDocument();
                if (linkedDoc == null)
                {
                    TaskDialog.Show("Zoom to Room", "Linked document is not available (unloaded).");
                    return Result.Cancelled;
                }

                string roomIdText = SimpleInputWindow.Show("Room Selection", "Enter the Room Number / ID:", string.Empty);
                if (string.IsNullOrWhiteSpace(roomIdText))
                    return Result.Cancelled;

                var room = new FilteredElementCollector(linkedDoc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<SpatialElement>()
                    .OfType<Room>()
                    .FirstOrDefault(r => ZoomRevitHelper.ParameterMatches(r, settings, roomIdText));

                if (room == null)
                {
                    TaskDialog.Show("Zoom to Room", $"Room '{roomIdText}' not found in link '{settings.SelectedRevitLinkName}'.");
                    return Result.Cancelled;
                }

                if (!ZoomGeometryHelper.TryGetRoomXYBounds_Link(room, linkInst, out XYZ minXY, out XYZ maxXY))
                {
                    TaskDialog.Show("Zoom to Room", "Failed to compute room bounds.");
                    return Result.Failed;
                }

                ZoomGeometryHelper.NormalizeAndBufferRectangle(ref minXY, ref maxXY, 800);

                var view = uiDoc.ActiveView;
                if (!ZoomRevitHelper.IsPlanLike(view))
                {
                    TaskDialog.Show("Zoom to Room", "Open a floor plan, ceiling plan, engineering plan, or area plan view.");
                    return Result.Cancelled;
                }

                bool cropWasDisabled = false;
                string adjustmentNote = null;
                string cropLockedMessage = null;

                ZoomDeferredExecutor.RunAfterViewIsOpen(uiApp, view, // <- CHANGED, was a direct uiView lookup + ZoomAndCenterRectangle call
                    onEachTick: () =>
                    {
                        var uiView = uiDoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id);
                        if (uiView == null) return;

                        cropWasDisabled = ZoomCropHelper.EnsureRectangleVisible(host, view, minXY, maxXY, out cropLockedMessage, out adjustmentNote);
                        uiView.ZoomAndCenterRectangle(minXY, maxXY);
                    },
                    onFinished: () =>
                    {
                        if (cropLockedMessage != null)
                            TaskDialog.Show("Zoom to Room", cropLockedMessage);

                        string resultMessage = $"Zoomed to '{roomIdText}' in link '{settings.SelectedRevitLinkName}'.";
                        if (cropWasDisabled)
                            resultMessage += " " + adjustmentNote;
                        TaskDialog.Show("Zoom to Room", resultMessage);
                    });

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}