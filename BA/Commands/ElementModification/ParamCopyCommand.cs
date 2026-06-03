using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BATools.ParamCopy.Views;

namespace BATools.ParamCopy.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ParamCopyCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            ParamCopyWindow.ShowOrFocus(commandData.Application);
            return Result.Succeeded;
        }
    }
}
