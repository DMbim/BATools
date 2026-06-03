using System;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using BATools.Zoom.Helpers;
using BATools.Zoom.Services;
using BATools.Zoom.Settings;
using BATools.Zoom.Views;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BATools.Zoom.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    public class Cmd_ZoomToRoom : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            var view = doc.ActiveView;

            if (!ZoomRevitHelper.IsPlanLike(view))
            {
                TaskDialog.Show("Zoom to Room", "The active view must be a floor plan, ceiling plan, engineering plan, or area plan.");
                return Result.Failed;
            }

            var settings = ZoomToRoomSettings.Load();

            bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            ZoomSettingsService.EnsureRoomIdParameterSelected(doc, settings, forcePrompt: shift);
            if (shift) settings.Save();

            var uiView = uiDoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id);
            if (uiView == null)
            {
                TaskDialog.Show("Zoom to Room", "UIView for the active view not found.");
                return Result.Failed;
            }

            while (true)
            {
                string roomIdText = SimpleInputWindow.Show("Room Selection", "Enter the Room Number / ID:", string.Empty);
                if (string.IsNullOrWhiteSpace(roomIdText))
                    return Result.Cancelled;

                var room = new FilteredElementCollector(doc)
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

                if (!ZoomGeometryHelper.TryGetRoomXYBounds_Local(room, out XYZ minXY, out XYZ maxXY))
                {
                    TaskDialog.Show("Zoom to Room", "Failed to compute room bounds.");
                    continue;
                }

                ZoomGeometryHelper.NormalizeAndBufferRectangle(ref minXY, ref maxXY, 800);
                uiView.ZoomAndCenterRectangle(minXY, maxXY);

                var td = new TaskDialog("Zoom to Room");
                td.MainInstruction = "Zoom to another room?";
                td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                if (td.Show() != TaskDialogResult.Yes) break;
            }

            return Result.Succeeded;
        }
    }
}