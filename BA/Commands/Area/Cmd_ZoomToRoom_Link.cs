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

                bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

                if (shift || string.IsNullOrWhiteSpace(settings.SelectedRevitLinkName))
                {
                    var picked = PickRevitLinkInstance(host);
                    if (picked == null) return Result.Cancelled;
                    settings.SelectedRevitLinkName = picked.Name;
                    ZoomSettingsService.EnsureRoomIdParameterSelected(
                        picked.GetLinkDocument() ?? host, settings, forcePrompt: true);
                    settings.Save();
                }
                else
                {
                    ZoomSettingsService.EnsureRoomIdParameterSelected(host, settings, forcePrompt: false);
                }

                var linkInst = new FilteredElementCollector(host)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .FirstOrDefault(li => string.Equals(
                        li.Name, settings.SelectedRevitLinkName, StringComparison.OrdinalIgnoreCase));

                if (linkInst == null)
                {
                    TaskDialog.Show("Zoom to Room", $"Saved link '{settings.SelectedRevitLinkName}' not found.");
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

                var uiView = uiDoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id);
                if (uiView == null)
                {
                    TaskDialog.Show("Zoom to Room", "UIView not found for the active view.");
                    return Result.Failed;
                }

                uiView.ZoomAndCenterRectangle(minXY, maxXY);
                TaskDialog.Show("Zoom to Room", $"Zoomed to '{roomIdText}' in link '{settings.SelectedRevitLinkName}'.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Shows SelectLinkWindow when multiple links exist, returns immediately when only one exists.
        /// Returns null if no links are found or the user cancels.
        /// </summary>
        private static RevitLinkInstance? PickRevitLinkInstance(Document host)
        {
            var links = new FilteredElementCollector(host)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .OrderBy(li => li.Name)
                .ToList();

            if (links.Count == 0)
            {
                TaskDialog.Show("Zoom to Room", "No Revit links in the model.");
                return null;
            }

            if (links.Count == 1) return links[0];

            var win = new SelectLinkWindow(links);
            return win.ShowDialog() == true ? win.SelectedLink : null;
        }
    }
}