// File: BA.UI/ViewTemplates/TemplateTransferWindow.xaml.cs
using Autodesk.Revit.UI;
using BA.UI.ExternalEvents;
using System;
using System.Windows;

namespace BA.UI.ViewTemplates
{
    public partial class TemplateTransferWindow : Window
    {
        private static TemplateTransferWindow? _instance;

        public TemplateTransferWindow(UIApplication uiApp)
        {
            if (uiApp == null) throw new ArgumentNullException(nameof(uiApp));
            InitializeComponent();
            DataContext = new TemplateTransferViewModel(uiApp, this);
        }

        public static TemplateTransferWindow GetOrCreate(UIApplication uiApp)
        {
            if (_instance == null || !_instance.IsLoaded)
                _instance = new TemplateTransferWindow(uiApp);
            return _instance;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}