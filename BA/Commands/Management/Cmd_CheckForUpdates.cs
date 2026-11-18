// FILE: BA/Commands/Management/Cmd_CheckForUpdates.cs
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Updates;
using System;

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
                var revitVersion = uiapp.Application.VersionNumber;

                if (!InstallerLauncher.InstallerExists())
                {
                    TaskDialog.Show("BA Tools Updater",
                        "The BA Tools installer is missing on this computer and will be downloaded now.\n\n" +
                        "Revit may pause for a short moment. Click Close to continue.");
                }

                if (!InstallerLauncher.TryLaunchRibbonWindow(revitVersion, out var error))
                {
                    TaskDialog.Show("BA Tools Updater", error ?? "The installer could not be started.");
                    return Result.Failed;
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Cmd_CheckForUpdates.Run", ex);
                TaskDialog.Show("BA Tools Updater", "Unexpected error:\n" + ex.Message);
                return Result.Failed;
            }
        }
    }
}