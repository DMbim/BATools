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
    public class Cmd_ZoomToRoom : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;
            var view = doc.ActiveView;

            if (!ZoomRevitHelper.IsPlanLike(view))
            {
                TaskDialog.Show("Zoom to Room", "The active view must be a floor plan, ceiling plan, engineering plan, or area plan.");
                return Result.Failed;
            }

            var settings = ZoomToRoomSettings.Load();

            PromptForRoomAndZoom(uiApp, uiDoc, doc, view, settings);

            return Result.Succeeded;
        }

        /// <summary>
        /// Prompts for a room id, zooms to it via ZoomDeferredExecutor, and on completion asks whether
        /// to zoom to another room, recursing into itself if so. Each call fully returns before the user
        /// can answer "zoom to another room?", since that answer only arrives later from inside the
        /// onFinished callback on a later Idling tick, so this does not grow the call stack the way a
        /// tight synchronous loop would.
        /// </summary>
        private static void PromptForRoomAndZoom(UIApplication uiApp, UIDocument uiDoc, Document doc, View view, ZoomToRoomSettings settings)
        {
            Room room;
            XYZ minXY, maxXY;

            while (true)
            {
                string roomIdText = SimpleInputWindow.Show("Room Selection", "Enter the Room Number / ID:", string.Empty);
                if (string.IsNullOrWhiteSpace(roomIdText))
                    return;

                room = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<SpatialElement>()
                    .OfType<Room>()
                    .FirstOrDefault(r => ZoomRevitHelper.ParameterMatches(r, settings, roomIdText));

                if (room == null)
                {
                    TaskDialog.Show("Zoom to Room", $"Room '{roomIdText}' not found.");
                    continue;
                }

                if (!ZoomGeometryHelper.TryGetRoomXYBounds_Local(room, out minXY, out maxXY))
                {
                    TaskDialog.Show("Zoom to Room", "Failed to compute room bounds.");
                    continue;
                }

                break;
            }

            ZoomGeometryHelper.NormalizeAndBufferRectangle(ref minXY, ref maxXY, 800);

            bool cropWasDisabled = false;
            string adjustmentNote = null;
            string cropLockedMessage = null;

            ZoomDeferredExecutor.RunAfterViewIsOpen(uiApp, view, // <- CHANGED, was uiDoc.ShowElements
                onEachTick: () =>
                {
                    var uiView = uiDoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id);
                    if (uiView == null) return;

                    cropWasDisabled = ZoomCropHelper.EnsureRectangleVisible(doc, view, minXY, maxXY, out cropLockedMessage, out adjustmentNote); // <- CHANGED, argument order fixed
                    uiView.ZoomAndCenterRectangle(minXY, maxXY);
                },
                onFinished: () =>
                {
                    if (cropLockedMessage != null)
                        TaskDialog.Show("Zoom to Room", cropLockedMessage);

                    var td = new TaskDialog("Zoom to Room");
                    td.MainInstruction = "Zoom to another room?";
                    td.MainContent = cropWasDisabled ? adjustmentNote : null;
                    td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;

                    if (td.Show() == TaskDialogResult.Yes)
                    {
                        PromptForRoomAndZoom(uiApp, uiDoc, doc, view, settings);
                    }
                });
        }
    }
}