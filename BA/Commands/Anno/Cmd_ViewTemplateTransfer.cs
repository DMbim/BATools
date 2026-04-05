using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.UI.ViewTemplates;
using System;
using System.Windows.Interop;

namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class Cmd_ViewTemplateTransfer : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;

                var window = new TemplateTransferWindow(uiApp);

                // Correct for Revit-hosted WPF:
                new WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;

                window.ShowDialog();
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