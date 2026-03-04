using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.App.Settings;              // PluginToggleRegistry
using BA.Core.Settings;             // ToggleBinding
using BA.UI.ExternalEvents;
using BA.UI.Helpers;
using BA.UI.Parameters;
using BA.UI.Settings;
using BA.UI.Views;
using System;
using System.Collections.Generic;
using System.Windows;

namespace BA.UI.Management
{
    public partial class ProjectToolsHubWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly RevitExternalInvoker _revit;

        private IReadOnlyList<ToggleBinding> _bindings = Array.Empty<ToggleBinding>();

        private Document? ActiveDoc => _uiApp.ActiveUIDocument?.Document;

        public ProjectToolsHubWindow(UIApplication uiApp, RevitExternalInvoker revit)
        {
            InitializeComponent();

            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _revit = revit ?? throw new ArgumentNullException(nameof(revit));

            RevitWindowHelper.SetOwnerToRevit(this, _uiApp);

            // ✅ Build the toggle list used by PluginSettingsWindow
            _bindings = PluginToggleRegistry.Build();
        }

        private void BtnParameterManager_Click(object sender, RoutedEventArgs e)
        {
            var doc = ActiveDoc;
            if (doc == null) { TaskDialog.Show("BA - Project Tools", "No active document."); return; }

            var wnd = new ParameterManagerWindow(_uiApp, doc, _revit);
            RevitWindowHelper.SetOwnerToRevit(wnd, _uiApp);
            wnd.Show();
            wnd.Activate();
        }

        private void BtnTemplateChecker_Click(object sender, RoutedEventArgs e)
        {
            var doc = ActiveDoc;
            if (doc == null) { TaskDialog.Show("BA - Project Tools", "No active document."); return; }

            var wnd = new TemplateCheckerWindow(_uiApp, doc);
            RevitWindowHelper.SetOwnerToRevit(wnd, _uiApp);
            wnd.Show();
            wnd.Activate();
        }

        private void BtnColour_Click(object sender, RoutedEventArgs e)
        {
            var doc = ActiveDoc;
            if (doc == null) { TaskDialog.Show("BA - Project Tools", "No active document."); return; }

            var wnd = BAViewFilterColorManager.GetOrCreate(_uiApp, _revit);
            RevitWindowHelper.SetOwnerToRevit(wnd, _uiApp);
            wnd.Show();
            wnd.Activate();
        }

        private void BtnFamilyTools_Click(object sender, RoutedEventArgs e)
        {
            TaskDialog.Show("BA - Project Tools", "Family tools not implemented yet.");
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var doc = ActiveDoc;
            if (doc == null) { TaskDialog.Show("BA - Project Tools", "No active document."); return; }

            // Safety: rebuild if something cleared it
            if (_bindings == null || _bindings.Count == 0)
                _bindings = PluginToggleRegistry.Build();

            var wnd = new PluginSettingsWindow(_bindings, _uiApp, doc);
            RevitWindowHelper.SetOwnerToRevit(wnd, _uiApp);

            // Settings window should be modal typically
            wnd.ShowDialog();
        }

        private void BtnCleanUpAndMaintenance_Click(object sender, RoutedEventArgs e)
        {
            TaskDialog.Show("BA - Project Tools", "Clean up and Maintenance not implemented yet.");
        }
    }
}