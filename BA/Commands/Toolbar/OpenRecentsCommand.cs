using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BATools.SelectionManager.Infrastructure;

namespace BATools.SelectionManager.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenRecentsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
                              ref string message, ElementSet elements)
        {
            try
            {
                SelectionManagerActivator.Instance.OpenRecents();
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