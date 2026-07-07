using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BATools.SelectionManager.Infrastructure;
using BATools.SelectionManager.Models;
using BATools.SelectionManager.Services;
using BATools.SelectionManager.Views;

namespace BATools.SelectionManager.ViewModels
{
    /// <summary>
    /// Compact sets list shown in the Quick Toolbar Sets tab.
    /// Reuses SetRowViewModel for health display.
    /// Refreshes automatically when the tab is selected.
    /// </summary>
    public class SetsToolbarTabViewModel : ObservableObject
    {
        // Fixed ID — this tab is always present, not persisted as a profile tab
        public Guid Id { get; } = new Guid("B0B0B0B0-0000-0000-0000-000000000001");
        public string Name { get; } = "Sets";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                SetProperty(ref _isSelected, value);
                if (value) Refresh();
            }
        }

        public ObservableCollection<SetRowViewModel> Sets { get; } = new();

        private string _statusMessage = "No sets loaded";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand SaveCurrentCommand { get; }

        public SetsToolbarTabViewModel()
        {
            SaveCurrentCommand = new RelayCommand(ExecuteSave);
            SetHealthMonitor.Instance.HealthChanged += OnHealthChanged;
        }

        /// <summary>Reload from repository — called when tab becomes selected.</summary>
        public void Refresh()
        {
            var sets = SetRepository.Instance.GetAll();
            Sets.Clear();

            foreach (var set in sets.OrderByDescending(s => s.Modified))
            {
                var vm = SetRowViewModel.FromModel(set);
                WireRowCommands(vm);
                Sets.Add(vm);
            }

            StatusMessage = Sets.Count == 0 ? "No saved sets" : $"{Sets.Count} set(s)";
        }

        private void WireRowCommands(SetRowViewModel vm)
        {
            vm.RecallCommand = new RelayCommand(() =>
                SelectionManagerBridge.Instance.RequestRecall(vm.Id));

            vm.DeleteCommand = new RelayCommand(() =>
            {
                SetRepository.Instance.Delete(vm.Id);
                Sets.Remove(vm);
                StatusMessage = $"{Sets.Count} set(s)";
            });
        }

        private void ExecuteSave()
        {
            var dialog = new SaveSetDialog();
            if (dialog.ShowDialog() != true) return;

            string name = dialog.SetName;
            if (string.IsNullOrWhiteSpace(name)) return;

            SelectionManagerBridge.Instance.RequestSaveCurrentSelection(name, newSet =>
            {
                // Already on WPF thread via Dispatcher.BeginInvoke in handler
                var vm = SetRowViewModel.FromModel(newSet);
                WireRowCommands(vm);
                Sets.Insert(0, vm);
                StatusMessage = $"{Sets.Count} set(s)";
            });
        }

        private void OnHealthChanged(Guid setId, SetHealthStatus status, int staleCount)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                var vm = Sets.FirstOrDefault(s => s.Id == setId);
                if (vm == null) return;
                vm.HealthStatus = status;
                vm.StaleCount = staleCount;
            }));
        }
    }
}