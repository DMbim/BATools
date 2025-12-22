using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.UI;
using System.Windows;
using BA.UI.Parameters;

namespace BA.UI.Management
{
    public partial class ProjectToolsHubWindow : Window
    {
        private readonly ExternalCommandData _cmd;
        private readonly UIApplication _uiApp;
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;

        public ProjectToolsHubWindow(ExternalCommandData cmd)
        {
            InitializeComponent();

            _cmd = cmd;
            _uiApp = cmd.Application;
            _uiDoc = _uiApp.ActiveUIDocument;
            _doc = _uiDoc?.Document;
        }

        private void BtnParameterManager_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null)
            {
                TaskDialog.Show("BA – Project Tools", "No active document.");
                return;
            }

            var wnd = new ParameterManagerWindow(_uiApp, _doc) { Owner = this };
            wnd.ShowDialog();
        } 

        private void BtnTemplateTools_Click(object sender, RoutedEventArgs e)
        {
            TaskDialog.Show("BA – Project Tools", "Template tools not implemented yet.");
        }

        private void BtnFamilyTools_Click(object sender, RoutedEventArgs e)
        {
            TaskDialog.Show("BA – Project Tools", "Family tools not implemented yet.");
        }
    }
}
