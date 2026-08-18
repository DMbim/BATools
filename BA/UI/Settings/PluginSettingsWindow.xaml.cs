using Autodesk.Revit.UI;
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
            // 1) Apply to runtime (your guards)
            foreach (var r in Rows)
            {
                r.Setter(r.Value);
            }

            // 2) Persist
            foreach (var r in Rows)
                _settings.SetBool(r.Key, r.Value);

            if (saveToDisk)
                PluginSettingsStore.Save(_settings, _path);
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