using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Services.Parameters;

namespace BA.Addin.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class InitializeParametersCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var document = commandData.Application.ActiveUIDocument.Document;
            var service = new SharedParameterService();

            try
            {
                using var tx = new Transaction(document, "CZA — Inicializace parametrů");
                tx.Start();
                service.EnsureSharedParametersExist(document);
                tx.Commit();

                TaskDialog.Show(
                    "Czech Area Compliance",
                    "Sdílené parametry byly úspěšně inicializovány.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Chyba při inicializaci parametrů: {ex.Message}";
                return Result.Failed;
            }
        }
    }
}