using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using BA.Core.Content.Models;
using BA.Core.Content.Services;

namespace BA.UI.LoadedFamilyBrowser
{
    public partial class LoadedFamilyBrowserSettingsWindow : Window
    {
        private sealed class CategoryToggle : INotifyPropertyChanged
        {
            private bool _isEnabled;

            public string Name { get; set; } = string.Empty;

            public bool IsEnabled
            {
                get => _isEnabled;
                set
                {
                    if (_isEnabled == value)
                        return;
                    _isEnabled = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private readonly LoadedFamilyBrowserSettingsService _settingsService;
        private readonly LoadedFamilyBrowserSettings _settings;
        private readonly ObservableCollection<CategoryToggle> _toggles = new();

        public bool SettingsChanged { get; private set; }

        public LoadedFamilyBrowserSettingsWindow(
            IEnumerable<string> knownCategoryNames,
            LoadedFamilyBrowserSettingsService settingsService,
            LoadedFamilyBrowserSettings settings,
            IntPtr ownerHandle)
        {
            InitializeComponent();

            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            if (ownerHandle != IntPtr.Zero)
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = ownerHandle;
            }

            foreach (string name in knownCategoryNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                bool enabled = !_settings.CategoryFilter.TryGetValue(name, out bool value) || value;
                _toggles.Add(new CategoryToggle { Name = name, IsEnabled = enabled });
            }

            CategoryItemsControl.ItemsSource = _toggles;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var toggle in _toggles)
                _settings.CategoryFilter[toggle.Name] = toggle.IsEnabled;

            _settingsService.Save(_settings);
            SettingsChanged = true;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}