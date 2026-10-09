using Autodesk.Revit.UI;
using BA.Core.Parameters;
using BA.Core.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace BA.UI.Settings
{
    public partial class PluginSettingsWindow : Window
    {
        private readonly IReadOnlyList<ToggleBinding> _bindings;
        private readonly PluginSettings _settings;
        private readonly string _path;
        private readonly UIApplication _uiApp;
        private readonly Document _doc;
        public ObservableCollection<ToggleRow> Rows { get; } = new();

        public PluginSettingsWindow(IReadOnlyList<ToggleBinding> bindings, UIApplication uiApp, Document doc, string? settingsPath = null)
        {
            InitializeComponent();

            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _path = settingsPath ?? PluginSettingsStore.GetDefaultPath();
            _settings = PluginSettingsStore.Load(_path);

            TxtCurrentVersion.Text = $"Version: {ReadCurrentVersion()}"; // <- NEW

            // Build UI rows
            foreach (var b in _bindings.OrderBy(x => x.Group).ThenBy(x => x.Name))
            {
                var value = _settings.GetBool(b.Key, b.DefaultValue);
                Rows.Add(new ToggleRow(b, value));
            }

            GridToggles.ItemsSource = Rows;

            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(GridToggles.ItemsSource);
            if (view != null)
            {
                view.GroupDescriptions?.Clear();
                view.GroupDescriptions?.Add(new System.Windows.Data.PropertyGroupDescription(nameof(ToggleRow.Group)));
            }

            // Shared parameter file path, defaults to the confirmed WIP2 default if
            // this settings.json has never had this key written to it.
            TxtWip2Path.Text = _settings.GetString(SharedParamPaths.SettingsKeyWip2Path, SharedParamPaths.DefaultWip2);
            RefreshWip2Warning();

            RefreshLeadersButton();
        }

        // Reads the same BATools.version file Publish.ps1 writes into the build
        // output, from whatever directory this assembly is actually running
        // from. Mirrors exactly what the installer's "Current version" shows,
        // so the two surfaces never disagree.
        private static string ReadCurrentVersion()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrWhiteSpace(dir)) return "unknown";

                var versionFile = System.IO.Path.Combine(dir, "BATools.version");
                return System.IO.File.Exists(versionFile)
                    ? System.IO.File.ReadAllText(versionFile).Trim()
                    : "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ApplySearch(TxtSearch.Text);
        }

        private void ApplySearch(string? text)
        {
            var s = (text ?? "").Trim();

            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(GridToggles.ItemsSource);
            if (view == null) return;

            if (string.IsNullOrWhiteSpace(s))
            {
                view.Filter = null;
                return;
            }

            view.Filter = obj =>
            {
                if (obj is not ToggleRow r) return false;
                return r.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                    || r.Group.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                    || r.Description.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                    || r.Key.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
            };
        }

        private void BtnEnableAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in Rows) r.Value = true;
        }

        private void BtnDisableAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in Rows) r.Value = false;
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in Rows)
                r.Value = r.DefaultValue;

            TxtWip2Path.Text = SharedParamPaths.DefaultWip2;
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            ApplyToRuntimeAndStore(saveToDisk: false);
        }

        private void BtnSaveClose_Click(object sender, RoutedEventArgs e)
        {
            ApplyToRuntimeAndStore(saveToDisk: true);
            DialogResult = true;
            Close();
        }

        private void ApplyToRuntimeAndStore(bool saveToDisk)
        {
            foreach (var r in Rows)
            {
                r.Setter(r.Value);
            }

            var wip2Path = (TxtWip2Path.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(wip2Path))
                wip2Path = SharedParamPaths.DefaultWip2;

            SharedParamPaths.SetWip2Path(wip2Path);

            foreach (var r in Rows)
                _settings.SetBool(r.Key, r.Value);

            _settings.SetString(SharedParamPaths.SettingsKeyWip2Path, wip2Path);

            if (saveToDisk)
                PluginSettingsStore.Save(_settings, _path);
        }

        private void TxtWip2Path_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            RefreshWip2Warning();
        }

        private void RefreshWip2Warning()
        {
            if (SharedParamPaths.TryValidateWip2(TxtWip2Path.Text, out var warning))
            {
                TxtWip2Warning.Text = "";
                TxtWip2Warning.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                TxtWip2Warning.Text = warning;
                TxtWip2Warning.Visibility = System.Windows.Visibility.Visible;
            }
        }

        private void BtnBrowseWip2_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Shared Parameter File (WIP2)",
                Filter = "Shared Parameter Files (*.txt)|*.txt|All files (*.*)|*.*",
                CheckFileExists = true
            };

            var current = TxtWip2Path.Text;
            if (!string.IsNullOrWhiteSpace(current))
            {
                try
                {
                    var dir = System.IO.Path.GetDirectoryName(current);
                    if (!string.IsNullOrWhiteSpace(dir) && System.IO.Directory.Exists(dir))
                        dlg.InitialDirectory = dir;
                }
                catch
                {
                }
            }

            if (dlg.ShowDialog(this) == true)
            {
                TxtWip2Path.Text = dlg.FileName;
            }
        }

        // ---------------- Revit Leaders ----------------

        private string ReadAutodeskUsername()
        {
            try
            {
                return (_uiApp.Application.Username ?? "").Trim();
            }
            catch
            {
                return "";
            }
        }

        // Enabled for a listed Revit Leader, or for anyone while the list is empty
        // (so the first leader can be registered). Disabled when the list cannot be read.
        private void RefreshLeadersButton()
        {
            try
            {
                LeaderRegistrySnapshot snapshot = RevitLeaderRegistry.Load();
                bool canOpen = RevitLeaderRegistry.CanEdit(snapshot, ReadAutodeskUsername());

                string tip;
                switch (snapshot.Status)
                {
                    case LeaderRegistryStatus.Unavailable:
                        tip = "The Revit Leader list is not reachable.";
                        break;
                    case LeaderRegistryStatus.Empty:
                        tip = "No leaders are registered yet. Open to register the first Revit Leader.";
                        break;
                    default:
                        tip = canOpen
                            ? "Manage the Revit Leaders who may create shared parameters."
                            : "Only listed Revit Leaders can manage this list.";
                        break;
                }

                BtnRevitLeaders.IsEnabled = canOpen;
                BtnRevitLeaders.ToolTip = tip;
            }
            catch
            {
                BtnRevitLeaders.IsEnabled = false;
                BtnRevitLeaders.ToolTip = "The Revit Leader list could not be checked.";
            }
        }

        private void BtnRevitLeaders_Click(object sender, RoutedEventArgs e)
        {
            var window = new RevitLeadersWindow(ReadAutodeskUsername()) { Owner = this };
            window.ShowDialog();

            RefreshLeadersButton();
        }
    }

    public sealed class ToggleRow : BA.UI.Mvvm.ObservableObject
    {
        private bool _value;

        public string Key { get; }
        public string Group { get; }
        public string Name { get; }
        public string Description { get; }
        public bool DefaultValue { get; }

        public Func<bool> Getter { get; }
        public Action<bool> Setter { get; }

        public BA.UI.Mvvm.RelayCommand ToggleCommand { get; }

        public ToggleRow(ToggleBinding binding, bool value)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));

            Key = binding.Key;
            Group = binding.Group;
            Name = binding.Name;
            Description = binding.Description;
            DefaultValue = binding.DefaultValue;

            Getter = binding.Getter;
            Setter = binding.Setter;

            _value = value;

            ToggleCommand = new BA.UI.Mvvm.RelayCommand(() => Value = !Value);
        }

        public bool Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }
    }
}