using BA.Core.Settings;
using System.Collections.ObjectModel;
using System.Windows;

namespace BA.UI.Settings
{
    public partial class RevitLeadersWindow : Window
    {
        private readonly string _currentUsername;
        private readonly ObservableCollection<string> _rows = new ObservableCollection<string>();

        public RevitLeadersWindow(string? currentUsername)
        {
            InitializeComponent();

            _currentUsername = RevitLeaderRegistry.Normalize(currentUsername);
            GridLeaders.ItemsSource = _rows;

            TxtCurrentUser.Text = _currentUsername.Length == 0
                ? "Your Autodesk account: not signed in"
                : "Your Autodesk account: " + _currentUsername;

            Reload();
        }

        private void Reload()
        {
            LeaderRegistrySnapshot snapshot = RevitLeaderRegistry.Load();

            _rows.Clear();
            foreach (string leader in snapshot.Leaders)
                _rows.Add(leader);

            bool canEdit = RevitLeaderRegistry.CanEdit(snapshot, _currentUsername);

            TxtNewLeader.IsEnabled = canEdit;
            BtnAdd.IsEnabled = canEdit;
            BtnAddMe.IsEnabled = canEdit && _currentUsername.Length > 0;
            BtnRemove.IsEnabled = canEdit;

            if (snapshot.Status == LeaderRegistryStatus.Unavailable)
            {
                ShowStatus("The leader list is not available: " + snapshot.Error);
            }
            else if (snapshot.Status == LeaderRegistryStatus.Empty)
            {
                ShowStatus("No Revit Leaders are registered yet. Add the first leader below.");
            }
            else if (!canEdit)
            {
                ShowStatus("Only listed Revit Leaders can change this list.");
            }
            else
            {
                ShowStatus("");
            }
        }

        private void ShowStatus(string text)
        {
            TxtStatus.Text = text ?? "";
            TxtStatus.Visibility = string.IsNullOrEmpty(text) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (RevitLeaderRegistry.TryAdd(TxtNewLeader.Text, _currentUsername, out string error))
            {
                TxtNewLeader.Text = "";
                Reload();
            }
            else
            {
                ShowStatus(error);
            }
        }

        private void BtnAddMe_Click(object sender, RoutedEventArgs e)
        {
            if (RevitLeaderRegistry.TryAdd(_currentUsername, _currentUsername, out string error))
            {
                Reload();
            }
            else
            {
                ShowStatus(error);
            }
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            string? selected = GridLeaders.SelectedItem as string;

            if (string.IsNullOrWhiteSpace(selected))
            {
                ShowStatus("Select a leader to remove.");
                return;
            }

            bool removingSelf = string.Equals(selected, _currentUsername, System.StringComparison.OrdinalIgnoreCase);

            if (removingSelf)
            {
                MessageBoxResult answer = MessageBox.Show(
                    this,
                    "You are removing your own account. You will lose access to this list and to shared parameter editing. Continue?",
                    "Remove yourself",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (answer != MessageBoxResult.Yes)
                    return;
            }

            if (RevitLeaderRegistry.TryRemove(selected, _currentUsername, out string error))
            {
                Reload();
            }
            else
            {
                ShowStatus(error);
            }
        }
    }
}