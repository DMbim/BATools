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
    public class SelectionManagerViewModel : ObservableObject
    {
        // ── Collections ──────────────────────────────────────────────────────────

        private ObservableCollection<SetRowViewModel> _sets = new();
        public ObservableCollection<SetRowViewModel> Sets
        {
            get => _sets;
            private set => SetProperty(ref _sets, value);
        }

        // ── Filter ───────────────────────────────────────────────────────────────

        private string _filterText = string.Empty;
        public string FilterText
        {
            get => _filterText;
            set
            {
                SetProperty(ref _filterText, value);
                ApplyFilter();
            }
        }

        private ObservableCollection<SetRowViewModel> _filteredSets = new();
        public ObservableCollection<SetRowViewModel> FilteredSets
        {
            get => _filteredSets;
            private set => SetProperty(ref _filteredSets, value);
        }

        // ── Status ───────────────────────────────────────────────────────────────

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // ── Commands ─────────────────────────────────────────────────────────────

        private readonly RelayCommand _saveCurrentSelectionCommand;
        public ICommand SaveCurrentSelectionCommand => _saveCurrentSelectionCommand;

        private readonly RelayCommand _clearFilterCommand;
        public ICommand ClearFilterCommand => _clearFilterCommand;

        // ─────────────────────────────────────────────────────────────────────────

        public SelectionManagerViewModel()
        {
            _saveCurrentSelectionCommand = new RelayCommand(ExecuteSaveCurrentSelection);
            _clearFilterCommand = new RelayCommand(() => FilterText = string.Empty);

            SetHealthMonitor.Instance.HealthChanged += OnHealthChanged;
        }

        public void LoadForDocument(string fingerprint)
        {
            SetRepository.Instance.LoadForDocument(fingerprint);
            RebuildList();
            StatusMessage = $"{Sets.Count} set(s) loaded.";
        }

        public void RebuildList()
        {
            var sets = SetRepository.Instance.GetAll();
            Sets.Clear();
            foreach (var set in sets.OrderByDescending(s => s.Modified))
            {
                var vm = SetRowViewModel.FromModel(set);
                WireRowCommands(vm);
                Sets.Add(vm);
            }
            ApplyFilter();
        }

        private void WireRowCommands(SetRowViewModel vm)
        {
            vm.RecallCommand = new RelayCommand<SetRowViewModel>(row =>
            {
                if (row == null) return;
                SelectionManagerBridge.Instance.RequestRecall(row.Id);
                StatusMessage = $"Recalled: {row.Name}";
            }, row => row != null);

            vm.DeleteCommand = new RelayCommand<SetRowViewModel>(row =>
            {
                if (row == null) return;
                SetRepository.Instance.Delete(row.Id);
                Sets.Remove(row);
                ApplyFilter();
                StatusMessage = $"Deleted: {row.Name}";
            }, row => row != null);

            vm.BeginRenameCommand = new RelayCommand<SetRowViewModel>(row =>
            {
                if (row != null) row.IsRenaming = true;
            }, row => row != null);

            vm.CommitRenameCommand = new RelayCommand<SetRowViewModel>(row =>
            {
                if (row == null) return;
                var set = SetRepository.Instance.GetById(row.Id);
                if (set == null) return;
                set.Name = row.Name;
                SetRepository.Instance.Update(set);
                row.IsRenaming = false;
                StatusMessage = $"Renamed to: {row.Name}";
            }, row => row != null);

            vm.AddToSetCommand = new RelayCommand<SetRowViewModel>(row =>
            {
                if (row == null) return;
                SelectionManagerBridge.Instance.RequestAddToSet(row.Id);
                StatusMessage = $"Added selection to: {row.Name}";
            }, row => row != null);

            // Wire vm as command parameter
            vm.RecallCommand = new RelayCommand(() =>
                SelectionManagerBridge.Instance.RequestRecall(vm.Id));
            vm.DeleteCommand = new RelayCommand(() =>
            {
                SetRepository.Instance.Delete(vm.Id);
                Sets.Remove(vm);
                ApplyFilter();
                StatusMessage = $"Deleted: {vm.Name}";
            });
            vm.BeginRenameCommand = new RelayCommand(() => vm.IsRenaming = true);
            vm.CommitRenameCommand = new RelayCommand(() =>
            {
                var set = SetRepository.Instance.GetById(vm.Id);
                if (set == null) return;
                set.Name = vm.Name;
                SetRepository.Instance.Update(set);
                vm.IsRenaming = false;
            });
            vm.AddToSetCommand = new RelayCommand(() =>
                SelectionManagerBridge.Instance.RequestAddToSet(vm.Id));
        }

        private void ExecuteSaveCurrentSelection()
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
                ApplyFilter();
                StatusMessage = $"Saved: {newSet.Name} ({newSet.UniqueIds.Count} element(s))";
            });
        }

        private void ApplyFilter()
        {
            FilteredSets.Clear();
            var filtered = string.IsNullOrWhiteSpace(FilterText)
                ? Sets
                : new ObservableCollection<SetRowViewModel>(
                    Sets.Where(s => s.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase)));

            foreach (var s in filtered)
                FilteredSets.Add(s);
        }

        private void OnHealthChanged(Guid setId, SetHealthStatus status, int staleCount)
        {
            // Already processed on a thread dispatched to UI from SetHealthMonitor
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