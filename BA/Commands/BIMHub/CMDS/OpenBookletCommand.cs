using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.Export.Infrastructure;
using BA.UI.Helpers;
using BA.ViewModels.Export;
using BA.Views.Export;

namespace BA.Commands.Export
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenBookletCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiApp, ref string message)
        {
            try
            {
                BookletUiBridge.EnsureInitialized();

                var viewModel = new BookletViewModel();
                var window = new BookletWindow(viewModel);
                RevitWindowHelper.SetOwnerToRevit(window, uiApp);
                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("OpenBookletCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
