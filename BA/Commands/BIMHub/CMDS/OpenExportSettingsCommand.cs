using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.Export.Infrastructure;
using BA.ViewModels.Export;
using BA.Views.Export;

namespace BA.Commands.Export
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenExportSettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiApp, ref string message)
        {
            try
            {
                // First-use lazy init, matches BA.Core.Ledger.LedgerUiBridge.EnsureInitialized(),
                // called here rather than at OnStartup since ExternalEvent.Create() needs a
                // valid Revit API context and this command's Execute() is one.
                ExportUiBridge.EnsureInitialized();

                var viewModel = new ExportSettingsViewModel();
                var window = new ExportSettingsWindow(viewModel);
                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("OpenExportSettingsCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
