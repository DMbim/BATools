// FILE: BA/Commands/Management/Cmd_CheckForUpdates.cs
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Updates;
using System;
using System.Threading.Tasks;

namespace BA.Commands.Management
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class Cmd_CheckForUpdates : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiapp, ref string message)
        {
            try
            {
                var result = Task.Run(() => UpdateService.ForceCheckAsync(uiapp))
                    .GetAwaiter()
                    .GetResult();

                // Back on the Revit UI thread here (GetResult() returned control to the
                // caller, it did not hop threads), so TaskDialog / Revit API calls are safe.
                // Passing the result explicitly, instead of relying on the cache, so a
                // failed or throttled check can't accidentally show a stale prior result.
                UpdateService.HandleForceCheckResult(result);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                AppLogger.LogError("Cmd_CheckForUpdates.Run", ex);
                TaskDialog.Show("BA Tools Update", "Update check failed:\n" + ex.Message);
                return Result.Failed;
            }
        }
    }
}