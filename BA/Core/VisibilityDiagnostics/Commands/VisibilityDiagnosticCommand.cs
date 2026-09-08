// File: BA_Tools/VisibilityDiagnostics/Commands/VisibilityDiagnosticCommand.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.UI.ExternalEvents;
using BA.VisibilityDiagnostics.ViewModels;
using BA.VisibilityDiagnostics.Views;

namespace BA.VisibilityDiagnostics.Commands
{
    /// <summary>
    /// Entry point for the Visibility Diagnostic Tool. Read-only by design (Phase 1
    /// is diagnosis only, no remediation) - opens/activates a modeless WPF window.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class VisibilityDiagnosticCommand : IExternalCommand
    {
        // Singleton window tracking: re-invoking the command activates the existing
        // modeless window instead of stacking duplicates. Static fields are safe here
        // because Revit add-in commands live for the whole application session and
        // this command only ever runs on the Revit UI thread.
        // <- CHANGED: _openViewModel added alongside _openWindow, so ShowForElement
        //    (below) can feed a new element into an already-open window without
        //    needing to know how VisibilityDiagnosticView exposes its DataContext.
        private static VisibilityDiagnosticView? _openWindow;
        private static VisibilityDiagnosticViewModel? _openViewModel;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                UIDocument? uiDoc = uiApp.ActiveUIDocument;
                if (uiDoc is null)
                {
                    message = "No active document.";
                    return Result.Failed;
                }

                if (_openWindow is { IsLoaded: true })
                {
                    _openWindow.Activate();
                    return Result.Succeeded;
                }

                VisibilityDiagnosticViewModel viewModel = CreateAndShowWindow(uiApp); // <- CHANGED: body extracted, same net effect as before

                ICollection<ElementId> selectedIds = uiDoc.Selection.GetElementIds();
                if (selectedIds.Count == 1)
                {
                    viewModel.PrefillFromInitialSelection(selectedIds.First());
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// NEW. Shared entry point for callers outside this command that need the
        /// diagnostic window opened and pointed at a specific element -
        /// VisibilityWarningWatcher, reacting to Revit's "elements not visible"
        /// warning. Activates the existing modeless window if one is already open
        /// (feeding it the new element instead of opening a second window), otherwise
        /// creates one - shares _openWindow/_openViewModel with Execute() above so the
        /// two entry points can never desync into two separate windows.
        ///
        /// Unlike PrefillFromInitialSelection (which only fills the text box and waits
        /// for the user to click Diagnose), this also runs diagnosis immediately - the
        /// user already confirmed via the warning's TaskDialog that they want to see
        /// the result, so an extra required click would be friction, not caution.
        ///
        /// MUST be called from a valid Revit API context on the Revit UI thread (an
        /// Idling callback, same as VisibilityWarningWatcher uses - not from within
        /// FailuresProcessing itself, since the document may not have finished
        /// committing yet at that point).
        /// </summary>
        public static void ShowForElement(UIApplication uiApplication, ElementId elementId)
        {
            VisibilityDiagnosticViewModel viewModel;
            if (_openWindow is { IsLoaded: true } && _openViewModel is not null)
            {
                _openWindow.Activate();
                viewModel = _openViewModel;
            }
            else
            {
                viewModel = CreateAndShowWindow(uiApplication);
            }

            viewModel.PrefillFromInitialSelection(elementId);
            viewModel.DiagnoseCommand.Execute(null); // ICommand.Execute - standard interface member, same one WPF invokes on a button click
        }

        private static VisibilityDiagnosticViewModel CreateAndShowWindow(UIApplication uiApplication)
        {
            // AppExternalInvoker.Instance must be first touched on the Revit UI
            // thread - both Execute() and ShowForElement's caller (an Idling
            // callback) satisfy that requirement.
            _ = AppExternalInvoker.Instance;

            var viewModel = new VisibilityDiagnosticViewModel();
            var window = new VisibilityDiagnosticView(viewModel);
            _ = new WindowInteropHelper(window) { Owner = uiApplication.MainWindowHandle };
            window.Closed += (_, _) =>
            {
                _openWindow = null;
                _openViewModel = null;
            };

            _openWindow = window;
            _openViewModel = viewModel;
            window.Show();

            return viewModel;
        }
    }
}