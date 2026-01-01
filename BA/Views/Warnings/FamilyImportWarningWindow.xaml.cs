using System.Collections.Generic;
using System.Windows;

namespace BA.UI.Views.Warnings
{
    public enum FamilyLoadMode
    {
        Cancel = 0,
        LoadAnyway = 1,
        FixAndLoad = 2
    }

    public sealed class FamilyImportDecision
    {
        public FamilyLoadMode Mode { get; set; } = FamilyLoadMode.Cancel;
        public bool SuppressForSession { get; set; }
        public bool AddBaPrefixInProject { get; set; }
        public bool CreateDefaultTypeIfNone { get; set; }
    }

    public partial class FamilyImportWarningWindow : Window
    {
        public FamilyImportDecision Decision { get; } = new FamilyImportDecision();

        public FamilyImportWarningWindow(
            string filePath,
            string familyName,
            string sizeText,
            string typesText,
            string cadInsideText,
            IEnumerable<string> flags,
            bool defaultAddPrefix,
            bool defaultCreateType,
            string thresholdText)
        {
            InitializeComponent();

            txtFile.Text = filePath;
            txtFamName.Text = familyName;
            txtSize.Text = sizeText;
            txtTypes.Text = typesText;
            txtCadInside.Text = cadInsideText;
            lstFlags.ItemsSource = flags;
            txtThreshold.Text = thresholdText;

            chkAddPrefix.IsChecked = defaultAddPrefix;
            chkCreateType.IsChecked = defaultCreateType;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Decision.Mode = FamilyLoadMode.Cancel;
            Decision.SuppressForSession = chkSuppressSession.IsChecked == true;
            Decision.AddBaPrefixInProject = chkAddPrefix.IsChecked == true;
            Decision.CreateDefaultTypeIfNone = chkCreateType.IsChecked == true;

            DialogResult = false;
            Close();
        }

        private void LoadAnyway_Click(object sender, RoutedEventArgs e)
        {
            Decision.Mode = FamilyLoadMode.LoadAnyway;
            Decision.SuppressForSession = chkSuppressSession.IsChecked == true;
            Decision.AddBaPrefixInProject = chkAddPrefix.IsChecked == true;
            Decision.CreateDefaultTypeIfNone = chkCreateType.IsChecked == true;

            DialogResult = true;
            Close();
        }

        private void FixAndLoad_Click(object sender, RoutedEventArgs e)
        {
            Decision.Mode = FamilyLoadMode.FixAndLoad;
            Decision.SuppressForSession = chkSuppressSession.IsChecked == true;
            Decision.AddBaPrefixInProject = chkAddPrefix.IsChecked == true;
            Decision.CreateDefaultTypeIfNone = chkCreateType.IsChecked == true;

            DialogResult = true;
            Close();
        }
    }
}
