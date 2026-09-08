// File: BA_Tools/CadPurge/Commands/CadPurgeCommand.cs
using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.CadPurge.Views;
using BA.UI.ExternalEvents;

namespace BA.CadPurge.Commands
{
    /// <summary>
    /// Ribbon entry point for CAD Purge. Opens a modeless window (see CadPurgeWindow), consistent
    /// with the rest of the AppExternalInvoker/RevitActionQueueHandler bridge pattern, which exists
    /// specifically to support modeless WPF windows calling back into the Revit API asynchronously.
    /// A single static window reference prevents opening a second instance: if one is already open,
    /// this just brings it to the front instead.
    ///
    /// CONFIRMED FIX: AppExternalInvoker.Instance is touched here, on purpose, before the window is
    /// shown. AppExternalInvoker.Instance is a lazily created, process wide static singleton, and
    /// the very first access to it calls ExternalEvent.Create(...). Revit requires that call to
    /// happen inside a standard API execution context (this Execute method qualifies). If CadPurge
    /// is the first BA command run in a given Revit session and AppExternalInvoker.Instance is left
    /// to initialize lazily from inside CadPurgeViewModel.Scan() instead, that first access happens
    /// from a WPF Button.Command callback on the modeless window, which is not a valid API execution
    /// context. Confirmed via journal.0004.0001.dmp: "Attempting to create an ExternalEvent outside
    /// of a standard API execution", immediately followed by an unrecoverable process termination.
    /// Referencing Instance here guarantees the singleton, and its underlying ExternalEvent, is
    /// created while still inside this command's own Execute call, every time, regardless of
    /// whether another BA feature has already warmed it up earlier in the session.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class CadPurgeCommand : IExternalCommand
    {
        private static CadPurgeWindow _openWindow;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Force AppExternalInvoker's static singleton to initialize now, inside a valid
                // API execution context, rather than deferring the first touch to whenever the
                // user first clicks a button inside the modeless window.
                _ = AppExternalInvoker.Instance;

                if (_openWindow != null)
                {
                    if (_openWindow.IsLoaded)
                    {
                        _openWindow.Activate();
                        return Result.Succeeded;
                    }

                    _openWindow = null;
                }

                _openWindow = new CadPurgeWindow();
                _openWindow.Closed += (_, __) => _openWindow = null;

                var interopHelper = new WindowInteropHelper(_openWindow)
                {
                    Owner = commandData.Application.MainWindowHandle
                };

                _openWindow.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("CadPurgeCommand.Execute", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}