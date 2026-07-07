// File: BA.UI/Commands/Management/Cmd_Settings.cs
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.App.Settings;
using BA.Core.Settings;
using BA.UI.Helpers;
using BA.UI.Settings;
using System;
using System.Collections.Generic;

namespace BA.UI.Commands.Management
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_Settings : IExternalCommand
    {
        // Correct type: IReadOnlyList<ToggleBinding> from BA.Core.Settings,
        // matching PluginSettingsWindow's constructor parameter exactly.
        private static IReadOnlyList<ToggleBinding>? _bindings;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = commandData.Application;

                var doc = uiApp.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("BA - Settings", "No active document.");
                    return Result.Cancelled;
                }

                if (_bindings == null || _bindings.Count == 0)
                    _bindings = PluginToggleRegistry.Build();

                var wnd = new PluginSettingsWindow(_bindings, uiApp, doc);
                RevitWindowHelper.SetOwnerToRevit(wnd, uiApp);
                wnd.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}