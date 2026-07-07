using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BATools.SelectionManager.Infrastructure;
using BATools.SelectionManager.Models;
using BATools.SelectionManager.Services;

namespace BATools.SelectionManager.ViewModels
{
    public class ToolbarSettingsViewModel : ObservableObject
    {
        // ── Options arrays for ComboBox binding ──────────────────────────────
        public ToggleShortcut[] ToggleOptions { get; } =
            (ToggleShortcut[])Enum.GetValues(typeof(ToggleShortcut));

        public FreezeShortcut[] FreezeOptions { get; } =
            (FreezeShortcut[])Enum.GetValues(typeof(FreezeShortcut));

        // ── Editable settings ────────────────────────────────────────────────
        private ToggleShortcut _toggleShortcut;
        public ToggleShortcut ToggleShortcut
        {
            get => _toggleShortcut;
            set => SetProperty(ref _toggleShortcut, value);
        }

        private FreezeShortcut _freezeShortcut;
        public FreezeShortcut FreezeShortcut
        {
            get => _freezeShortcut;
            set => SetProperty(ref _freezeShortcut, value);
        }

        private int _toggleWindowMs;
        public int ToggleWindowMs
        {
            get => _toggleWindowMs;
            set => SetProperty(ref _toggleWindowMs, value);
        }

        private int _freezeHoldMs;
        public int FreezeHoldMs
        {
            get => _freezeHoldMs;
            set => SetProperty(ref _freezeHoldMs, value);
        }
        private int _toolbarWidth;
        public int ToolbarWidth
        {
            get => _toolbarWidth;
            set => SetProperty(ref _toolbarWidth, value);
        }
        // ── Commands ─────────────────────────────────────────────────────────
        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }

        // Window subscribes to this event to close with DialogResult=true
        public event Action<bool>? CloseRequested;

        // ── Constructor ───────────────────────────────────────────────────────
        public ToolbarSettingsViewModel()
        {
            LoadFromDisk();
            SaveCommand = new RelayCommand(ExecuteSave);
            ResetCommand = new RelayCommand(ExecuteReset);

        }

        // ── Private ───────────────────────────────────────────────────────────
        private void LoadFromDisk()
        {
            HotkeySettings s = HotkeySettingsService.Load();
            _toggleShortcut = s.Toggle;
            _freezeShortcut = s.Freeze;
            _toggleWindowMs = s.ToggleWindowMs;
            _freezeHoldMs = s.FreezeHoldMs;
            _toolbarWidth = s.ToolbarWidth;
        }

        private void ExecuteSave()
        {
            HotkeySettingsService.Save(new HotkeySettings
            {
                Toggle = ToggleShortcut,
                Freeze = FreezeShortcut,
                ToggleWindowMs = ToggleWindowMs,
                FreezeHoldMs = FreezeHoldMs,
                ToolbarWidth = ToolbarWidth,
            });
            SelectionManagerActivator.Instance.ReloadHotkeySettings();
            CloseRequested?.Invoke(true);
        }

        private void ExecuteReset()
        {
            var defaults = new HotkeySettings();
            ToggleShortcut = defaults.Toggle;
            FreezeShortcut = defaults.Freeze;
            ToggleWindowMs = defaults.ToggleWindowMs;
            FreezeHoldMs = defaults.FreezeHoldMs;
            ToolbarWidth = defaults.ToolbarWidth;
        }
    }
}