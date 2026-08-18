// FILE: BA_Tools/Commands/Management/Cmd_OpenWarningsDashboard.cs
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Warnings.Views;

namespace BA.Commands.Management
{
    [Transaction(TransactionMode.Manual)]
    public class Cmd_OpenWarningsDashboard : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiApp, ref string message)
        {
            try
            {
                WarningsDashboardWindow.GetOrCreate(uiApp).Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Cmd_OpenWarningsDashboard.Run", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}