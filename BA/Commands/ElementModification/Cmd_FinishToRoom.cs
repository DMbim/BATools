// Refs:
// - PresentationFramework, PresentationCore, WindowsBase
// - System.Text.Json
// - RevitAPI, RevitAPIUI

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using BA.Core.Rooms;


namespace BA.Commands.Rooms
{
    [Transaction(TransactionMode.Manual)]
    public class Cmd_FinishToRoom : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiapp = commandData.Application;

                // ExternalEvent bridge
                var handler = new RoomHostFinishTransferHandler();
                var exEvent = ExternalEvent.Create(handler);

                // Show WPF window (modeless)
                var win = new RoomHostFinishTransferWindow(uiapp, exEvent, handler);
                win.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }
    }
}