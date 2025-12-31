using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.UI.ExternalEvents;
using BA.UI.Helpers;
using BA.UI.Parameters;
using BA.UI.Views;
using System;
using System.Windows;

namespace BA.UI.Management
{
    public partial class ProjectToolsHubWindow : Window
    {
        private readonly UIApplication _uiApp;

        private readonly RevitActionQueueHandler _handler;
        private readonly ExternalEvent _extEvent;
        private readonly RevitExternalInvoker _revit;

        private Document ActiveDoc => _uiApp.ActiveUIDocument?.Document;

        public ProjectToolsHubWindow(ExternalCommandData cmd)
        {
            InitializeComponent();

            _uiApp = cmd?.Application ?? throw new ArgumentNullException(nameof(cmd));
            RevitWindowHelper.SetOwnerToRevit(this, _uiApp);

            _handler = new RevitActionQueueHandler();
            _extEvent = ExternalEvent.Create(_handler);
            _revit = new RevitExternalInvoker(_handler, _extEvent, Dispatcher);
        }

        private void BtnParameterManager_Click(object sender, RoutedEventArgs e)
        {
            var doc = ActiveDoc;
            if (doc == null) { TaskDialog.Show("BA - Project Tools", "No active document."); return; }

            var wnd = new ParameterManagerWindow(_uiApp, doc);
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

        private void BtnCleanUpAndMaintenance_Click(object sender, RoutedEventArgs e)
        {
            TaskDialog.Show("BA - Project Tools", "Clean up and Maintenance not implemented yet.");
        }
    }
}
