using Autodesk.Revit.UI;
using System;
using System.Windows;

namespace BA.UI.ViewTemplates
{
    public partial class TemplateTransferWindow : Window
    {
        public TemplateTransferWindow(UIApplication uiApp)
        {
            InitializeComponent();

            if (uiApp == null) throw new ArgumentNullException(nameof(uiApp));

            DataContext = new TemplateTransferViewModel(uiApp, this);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}