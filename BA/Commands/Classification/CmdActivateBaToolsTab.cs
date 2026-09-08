// FILE: BA/Commands/Ribbon/CmdActivateBaToolsTab.cs
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Ribbon;

namespace BA.Commands.Ribbon
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdActivateBaToolsTab : IExternalCommand
    {
        private const string TargetTabInternalName = "BA_Tools";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string errorMessage = string.Empty;
            bool success = RibbonTabActivator.Activate(TargetTabInternalName, ref errorMessage);

            if (!success)
            {
                message = errorMessage;
                return Result.Failed;
            }

            return Result.Succeeded;
        }
    }
}