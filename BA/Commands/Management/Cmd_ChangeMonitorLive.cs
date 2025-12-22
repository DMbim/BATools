using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using BA.UI;

namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class Cmd_ChangeMonitorLive : ExternalCommand
    {
        public override void Execute()
        {
            try
            {
                LiveReviewHost.ShowOrActivate(UiApplication);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Change Monitor - Error", ex.ToString());
            }
        }
    }
}
