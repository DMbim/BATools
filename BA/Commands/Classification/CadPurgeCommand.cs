// File: BA_Tools/CadPurge/Commands/CadPurgeCommand.cs
using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.CadPurge.Views;

namespace BA.CadPurge.Commands
{
    /// <summary>
    /// Ribbon entry point for CAD Purge. Opens a modeless window (see CadPurgeWindow), consistent
    /// with the rest of the AppExternalInvoker/RevitActionQueueHandler bridge pattern, which exists
    /// specifically to support modeless WPF windows calling back into the Revit API asynchronously.
    /// A single static window reference prevents opening a second instance: if one is already open,
    /// this just brings it to the front instead.
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