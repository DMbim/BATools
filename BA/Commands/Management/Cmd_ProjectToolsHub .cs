// File: BA.Commands/Management/Cmd_ProjectToolsHub.cs
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.UI.ExternalEvents;
using BA.UI.Helpers;
using BA.UI.Management;
using System;
using System.Windows.Threading;

namespace BA.Commands.Management
{
    [Transaction(TransactionMode.Manual)]
    public sealed class Cmd_ProjectToolsHub : IExternalCommand
    {
        private static ProjectToolsHubWindow? _win;

        // Keep these static so they don't get GC'd and so every window shares one queue.
        private static RevitActionQueueHandler? _handler;
        private static ExternalEvent? _extEvent;
        private static RevitExternalInvoker? _revit;

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

                // Ensure ExternalEvent infrastructure exists (once per session)
                EnsureRevitInvoker(uiApp);

                // Create window once
                _win = new ProjectToolsHubWindow(uiApp, _revit!);
                RevitWindowHelper.SetOwnerToRevit(_win, uiApp);

                _win.Closed += (_, __) => _win = null;

                _win.Show(); // modeless
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }

        private static void EnsureRevitInvoker(UIApplication uiApp)
        {
            if (_revit != null && _handler != null && _extEvent != null)
                return;

            var dispatcher =
                System.Windows.Application.Current?.Dispatcher
                ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;

            _handler = new RevitActionQueueHandler(dispatcher);
            _extEvent = ExternalEvent.Create(_handler);
            _revit = new RevitExternalInvoker(_handler, _extEvent);
        }

    }
}