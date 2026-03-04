using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.UI.Finishes;
using BA.UI.Core.Finishes;
using System;

namespace BA.Commands.Finishes
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ApplyFinishesByRoomsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                UIDocument uiDoc = uiApp.ActiveUIDocument;
                Document doc = uiDoc.Document;

                if (doc.IsFamilyDocument)
                {
                    TaskDialog.Show("BA", "This tool works only in a project document.");
                    return Result.Cancelled;
                }

                var runner = new RevitExternalEventRunner(uiApp);

                var win = new ApplyFinishesByRoomsWindow(uiApp, runner);
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