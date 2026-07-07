using BA.Resources;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace BA.UI.Views.Warnings
{
    public enum ImportCadDecision
    {
        ContinueImport = 0,
        CancelImport = 1,
        UseLinkCad = 2
    }

    public partial class ImportCadWarningWindow : Window
    {
        public bool SuppressForSession { get; private set; }
        public ImportCadDecision Decision { get; private set; } = ImportCadDecision.ContinueImport;

        public ImportCadWarningWindow()
        {
            InitializeComponent();
            // In ImportCadWarningWindow constructor, after InitializeComponent():
            string installRoot = BaResourcePaths.GetInstallRoot();
            string photoPath = Path.Combine(installRoot, "Assets", "Images", "cad_warning.jpg");
            if (File.Exists(photoPath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(photoPath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                WarningPhoto.Source = bmp;
            }
        }

        private void UseLinkCad_Click(object sender, RoutedEventArgs e)
        {
            SuppressForSession = chkSuppress.IsChecked == true;
            Decision = ImportCadDecision.UseLinkCad;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            SuppressForSession = chkSuppress.IsChecked == true;
            Decision = ImportCadDecision.CancelImport;
            DialogResult = true;
            Close();
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            SuppressForSession = chkSuppress.IsChecked == true;
            Decision = ImportCadDecision.ContinueImport;
            DialogResult = true;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // treat X as "Cancel import" (safer default)
            Cancel_Click(sender, e);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { /* ignore */ }
            }
        }
    }
}
