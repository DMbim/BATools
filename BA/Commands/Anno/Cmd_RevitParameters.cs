// File: BA.UI/Commands/Anno/Cmd_RevitParameters.cs
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.UI.ExternalEvents;
using BA.UI.Helpers;
using BA.UI.Parameters;
using System;
using System.Windows.Threading;

namespace BA.UI.Commands.Anno
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_RevitParameters : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = commandData.Application;

                var doc = uiApp.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("BA - Parameters", "No active document.");
                    return Result.Cancelled;
                }

                // IExternalCommand.Execute() runs on the Revit main thread.
                // We still need an invoker for any subsequent Revit API calls
                // triggered from WPF button handlers inside the window, which
                // run on the WPF dispatcher thread.
                var handler = new RevitActionQueueHandler(Dispatcher.CurrentDispatcher);
                var externalEvent = Autodesk.Revit.UI.ExternalEvent.Create(handler);
                var invoker = new RevitExternalInvoker(handler, externalEvent);

                var wnd = new ParameterManagerWindow(uiApp, doc, invoker);
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