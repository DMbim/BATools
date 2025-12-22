using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BA.Filters;

namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class Cmd_RayBounceCeiling : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                RayBounce rayBounce = new RayBounce(uiApp, uiDoc, doc);
                rayBounce.CalculateRayBounce();
                        
                TaskDialog.Show("Success", "Ray bounce calculation completed.");            
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
            return Result.Succeeded;

        }
    }

}
