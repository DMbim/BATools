using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.AutoAnnotate.Views;
using BA.BIM.Core.Dimensioning.Infrastructure;

namespace BA.BIM.Commands.Dimension
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class BA_CmdAutoDimension : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            var bridge = BA_DimensionModule.Bridge;
            if (bridge == null)
            {
                message = "BA_DimensionModule.Bridge is not initialized. " +
                           "BA_DimensionModule.Initialize() must be called from BaApplication.OnStartup.";
                return Result.Failed;
            }

            var viewModel = new BA_AutoDimensionViewModel(uiDoc, bridge);
            var window = new BA_AutoDimensionView(viewModel);

            window.Show();

            return Result.Succeeded;
        }
    }
}