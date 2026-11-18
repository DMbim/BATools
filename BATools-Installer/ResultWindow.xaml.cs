// File: BATools-Installer/ResultWindow.xaml.cs
using System.Windows;

namespace BATools_Installer
{
    /// <summary>
    /// Modal popup shown when an install, update or uninstall ends. Success shows Close and the
    /// caller then shuts the whole installer down. Failure shows OK and returns to the window.
    /// </summary>
    public partial class ResultWindow : Window
    {
        public ResultWindow(string heading, string message, bool success)
        {
            InitializeComponent();

            Title = success ? "Finished" : "Error";
            HeadingText.Text = heading;
            MessageText.Text = message;
            CloseButton.Content = success ? "Close" : "OK";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}