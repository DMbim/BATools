using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using BA.Core;
using BA.UI;

namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class Cmd_ChangeMonitorStart : ExternalCommand
    {
        public override void Execute()
        {
            try
            {
                var uiApp = UiApplication;
                var uiDoc = UiDocument;
                var doc = Document;

                if (doc == null)
                {
                    TaskDialog.Show("Change Monitor", "No active document is available.");
                    return;
                }

                if (ChangeMonitorService.IsRunning)
                {
                    TaskDialog.Show("Change Monitor", "Monitoring is already running.");
                    return;
                }

                ChangeMonitorService.Start(uiApp);

                var td = new TaskDialog("Change Monitor")
                {
                    MainInstruction = "Monitoring started.",
                    MainContent = "All adds, deletes, moves, and parameter edits will be tracked.",
                    CommonButtons = TaskDialogCommonButtons.Ok
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open Live Review Window");

                var res = td.Show();
                if (res == TaskDialogResult.CommandLink1)
                {
                    LiveReviewHost.ShowOrActivate(uiApp);
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Change Monitor - Error", ex.ToString());
            }
        }
    }
}
