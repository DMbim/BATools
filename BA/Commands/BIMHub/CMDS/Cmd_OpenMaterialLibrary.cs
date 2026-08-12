// Path: BA\Materials\Cmd_OpenMaterialLibrary.cs
using System;
using System.Windows.Threading;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Materials.UI;
using BA.UI.ExternalEvents;

namespace BA.Materials
{
    /// <summary>
    /// Ribbon command entry point for the material library window. Follows the
    /// established lazy-initialization pattern, the ExternalEvent/handler/invoker
    /// triple is created on first Execute rather than eagerly at OnStartup, and is
    /// reused for the lifetime of the Revit session.
    ///
    /// Only one window instance is allowed open at a time, since LibraryDocumentService
    /// holds a write lock tied to a single session's open/close lifecycle, opening a
    /// second window from the same Revit process would either double-lock or silently
    /// fight over the same background document. Re-invoking the command while a window
    /// is already open activates the existing window instead of creating a new one.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class Cmd_OpenMaterialLibrary : IExternalCommand
    {
        private static RevitActionQueueHandler _handler;
        private static ExternalEvent _externalEvent;
        private static RevitExternalInvoker _invoker;
        private static MaterialLibraryWindow _openWindow;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                EnsureInvoker(uiApp);

                if (_openWindow != null)
                {
                    // Window still exists but may have been closed without going
                    // through the normal Closed event in some edge case, guard with
                    // IsLoaded rather than trusting the field alone.
                    if (_openWindow.IsLoaded)
                    {
                        _openWindow.Activate();
                        return Result.Succeeded;
                    }

                    _openWindow = null;
                }

                _openWindow = new MaterialLibraryWindow(uiApp, _invoker);
                _openWindow.Closed += (s, e) => _openWindow = null;
                _openWindow.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Cmd_OpenMaterialLibrary.Execute", ex);
                message = "Failed to open the material library. See BA Tools log for details.";
                return Result.Failed;
            }
        }

        /// <summary>
        /// BimHub integration entry point, per the established convention that commands
        /// integrating with BimHub expose this signature. Mirrors Execute's logic
        /// without the ExternalCommandData wrapper, ExternalCommandData is sealed and
        /// cannot be constructed here, this overload is called directly by BimHub with
        /// a UIApplication it already has.
        /// </summary>
        public static Result Run(UIApplication uiApp, ref string message)
        {
            try
            {
                EnsureInvoker(uiApp);

                if (_openWindow != null)
                {
                    if (_openWindow.IsLoaded)
                    {
                        _openWindow.Activate();
                        return Result.Succeeded;
                    }

                    _openWindow = null;
                }

                _openWindow = new MaterialLibraryWindow(uiApp, _invoker);
                _openWindow.Closed += (s, e) => _openWindow = null;
                _openWindow.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Cmd_OpenMaterialLibrary.Run", ex);
                message = "Failed to open the material library. See BA Tools log for details.";
                return Result.Failed;
            }
        }

        private static void EnsureInvoker(UIApplication uiApp)
        {
            if (_invoker != null) return;

            // Must run on Revit's UI thread, which Execute/Run both already are,
            // Dispatcher.CurrentDispatcher captured here is the same thread WPF
            // windows shown modelessly from Revit run on.
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;

            _handler = new RevitActionQueueHandler(dispatcher);
            _externalEvent = ExternalEvent.Create(_handler);
            _invoker = new RevitExternalInvoker(_handler, _externalEvent);

            AppLogger.LogInfo("BA.Materials: initialized ExternalEvent/handler/invoker for material library.");
        }
    }
}