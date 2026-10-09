using System.Windows;

namespace BA.UI.Views.Warnings
{
    public enum SharedParameterBlockReason
    {
        NotLeader = 0,
        NotSignedIn = 1,
        RegistryUnavailable = 2,
        NoLeadersRegistered = 3
    }

    public partial class SharedParameterBlockedWindow : Window
    {
        public SharedParameterBlockedWindow(SharedParameterBlockReason reason, string? autodeskUsername)
        {
            InitializeComponent();

            TxtMessage.Text = BuildMessage(reason);

            string name = (autodeskUsername ?? "").Trim();
            TxtUser.Text = name.Length == 0
                ? "Autodesk account: not signed in"
                : "Autodesk account: " + name;
        }

        private static string BuildMessage(SharedParameterBlockReason reason)
        {
            switch (reason)
            {
                case SharedParameterBlockReason.NotSignedIn:
                    return "Revit could not read your Autodesk account, so your permission cannot be checked. Sign in to Autodesk in Revit and try again.";

                case SharedParameterBlockReason.RegistryUnavailable:
                    return "The Revit Leader list could not be read, so access is blocked for now. The network share may be unavailable or the list file may be damaged.";

                case SharedParameterBlockReason.NoLeadersRegistered:
                    return "No Revit Leaders are registered yet, so creating and editing shared parameters is blocked for everyone.";

                default:
                    return "Creating and editing shared parameters is restricted to Revit Leaders. Your Autodesk account is not on the Revit Leader list.";
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}