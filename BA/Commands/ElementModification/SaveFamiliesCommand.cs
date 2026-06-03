using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Families.Handlers;
using BA.Families.ViewModels;
using BA.Families.Views;

namespace BA.Families.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SaveFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
                              ref string message,
                              ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;

            if (uidoc is null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            Document doc = uidoc.Document;

            var handler = new SaveFamiliesHandler();
            var externalEvent = ExternalEvent.Create(handler);
            var viewModel = new SaveFamiliesViewModel(doc, externalEvent, handler);
            var view = new SaveFamiliesView(viewModel);

            // Modeless — returns immediately; ExternalEvent marshals saves back to Revit thread
            view.Show();

            return Result.Succeeded;
        }
    }
}