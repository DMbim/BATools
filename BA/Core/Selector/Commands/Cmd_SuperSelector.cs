// File: BA.UI/Commands/Anno/Cmd_SuperSelector.cs
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.UI.ExternalEvents;
using BA.UI.Helpers;
using BA.UI.Views;
using System;
using System.Windows.Threading;

namespace BA.UI.Commands.Anno
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_SuperSelector : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = commandData.Application;
                var doc = uiApp.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("BA - Super Selector", "No active document.");
                    return Result.Cancelled;
                }
                var handler = new RevitActionQueueHandler(Dispatcher.CurrentDispatcher);
                var externalEvent = Autodesk.Revit.UI.ExternalEvent.Create(handler);
                var invoker = new RevitExternalInvoker(handler, externalEvent);
                var wnd = BASuperSelector.GetOrCreate(uiApp, invoker);
                RevitWindowHelper.SetOwnerToRevit(wnd, uiApp);
                wnd.Show();
                wnd.Activate();
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