using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BATools.SelectionManager.ViewModels
{
    public class ToolbarTabViewModel : ObservableObject
    {
        // ── Identity ─────────────────────────────────────────────────────────────
        public Guid Id { get; }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        // ── Collections ──────────────────────────────────────────────────────────
        public ObservableCollection<QuickActionButtonViewModel> PinnedButtons { get; } = new();
        public ObservableCollection<ToolbarGroupViewModel> Groups { get; } = new();

        // ── Selected pinned button (for remove) ───────────────────────────────────
        private QuickActionButtonViewModel? _selectedPinned;
        public QuickActionButtonViewModel? SelectedPinned
        {
            get => _selectedPinned;
            set => SetProperty(ref _selectedPinned, value);
        }

        // ── Commands ──────────────────────────────────────────────────────────────
        public ICommand AddGroupCommand { get; }
        public ICommand RemovePinnedCommand { get; }

        // Assigned externally by QuickToolbarViewModel after construction
        private RelayCommand _addToPinnedCommand;
        public ICommand AddToPinnedCommand
        {
            get => _addToPinnedCommand;
            set { _addToPinnedCommand = (RelayCommand)value; OnPropertyChanged(); }
        }

        // ── Events ────────────────────────────────────────────────────────────────
        public event Action<ToolbarGroupViewModel>? AddFunctionToGroupRequested;

        // ── Constructor ───────────────────────────────────────────────────────────
        public ToolbarTabViewModel(Guid id, string name)
        {
            Id = id;
            _name = name;

            _addToPinnedCommand = new RelayCommand(() => { }); // replaced externally

            AddGroupCommand = new RelayCommand(ExecuteAddGroup);

            RemovePinnedCommand = new RelayCommand(
                () =>
                {
                    if (SelectedPinned != null)
                        PinnedButtons.Remove(SelectedPinned);
                },
                () => SelectedPinned != null);
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Called by QuickToolbarViewModel after adding a group to wire up its commands.
        /// </summary>
        public void WireGroupCommands(ToolbarGroupViewModel group)
        {
            group.MoveUpCommand = new RelayCommand(
                () => MoveGroup(group, -1),
                () => Groups.IndexOf(group) > 0);

            group.MoveDownCommand = new RelayCommand(
                () => MoveGroup(group, 1),
                () => Groups.IndexOf(group) < Groups.Count - 1);

            group.AddFunctionRequested += grp =>
                AddFunctionToGroupRequested?.Invoke(grp);
        }

        public System.Collections.Generic.List<string> GetPinnedActionIds()
            => PinnedButtons
               .Select(b => b.Action?.Id ?? string.Empty)
               .Where(s => !string.IsNullOrEmpty(s))
               .ToList();

        // ── Private ───────────────────────────────────────────────────────────────

        private void ExecuteAddGroup()
        {
            var group = new ToolbarGroupViewModel(Guid.NewGuid(), "New Group");
            WireGroupCommands(group);
            Groups.Add(group);
        }

        private void MoveGroup(ToolbarGroupViewModel group, int delta)
        {
            int idx = Groups.IndexOf(group);
            int newIdx = idx + delta;
            if (newIdx < 0 || newIdx >= Groups.Count) return;
            Groups.Move(idx, newIdx);
        }
    }
}