using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.DB;

namespace BA.Zoom.Views
{
    public partial class SelectLinkWindow : Window
    {
        /// <summary>
        /// The link instance chosen by the user. Null if cancelled.
        /// </summary>
        public RevitLinkInstance? SelectedLink { get; private set; }

        public SelectLinkWindow(IEnumerable<RevitLinkInstance> links)
        {
            InitializeComponent();
            CmbLinks.ItemsSource = links;
            CmbLinks.SelectedIndex = 0;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            SelectedLink = CmbLinks.SelectedItem as RevitLinkInstance;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedLink = null;
            DialogResult = false;
            Close();
        }
    }
}