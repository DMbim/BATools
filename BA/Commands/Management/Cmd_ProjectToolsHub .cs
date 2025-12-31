// File: BA.Commands/Management/Cmd_ProjectToolsHub.cs
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.UI.Helpers;
using BA.UI.Management;
using System;

namespace BA.Commands.Management
{
    [Transaction(TransactionMode.Manual)]
    public sealed class Cmd_ProjectToolsHub : IExternalCommand
    {
        private static ProjectToolsHubWindow? _win;

        public Result Execute(ExternalCommandData c, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = c?.Application;
                if (uiApp == null)
                {
                    message = "UIApplication not available.";
                    return Result.Failed;
                }

                // If already open, just bring to front
                if (_win != null)
                {
                    if (_win.WindowState == System.Windows.WindowState.Minimized)
                        _win.WindowState = System.Windows.WindowState.Normal;

                    _win.Activate();
                    return Result.Succeeded;
                }

                _win = new ProjectToolsHubWindow(c);
                RevitWindowHelper.SetOwnerToRevit(_win, uiApp);

                _win.Closed += (_, __) => _win = null;

                _win.Show(); // IMPORTANT: modeless
                return Result.Succeeded; // IMPORTANT: command returns immediately
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }
    }
}
