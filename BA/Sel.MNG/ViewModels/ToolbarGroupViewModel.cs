using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BATools.SelectionManager.ViewModels
{
    public class ToolbarGroupViewModel : ObservableObject
    {
        public Guid Id { get; }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public ObservableCollection<QuickActionButtonViewModel> Items { get; } = new();

        public ICommand ToggleExpandCommand { get; }
        public ICommand AddFunctionCommand { get; }

        public event Action<ToolbarGroupViewModel>? AddFunctionRequested;

        private RelayCommand _moveUpCommand;
        public ICommand MoveUpCommand
        {
            get => _moveUpCommand;
            set { _moveUpCommand = (RelayCommand)value; OnPropertyChanged(); }
        }

        private RelayCommand _moveDownCommand;
        public ICommand MoveDownCommand
        {
            get => _moveDownCommand;
            set { _moveDownCommand = (RelayCommand)value; OnPropertyChanged(); }
        }

        public ToolbarGroupViewModel(Guid id, string name)
        {
            Id = id;
            _name = name;

            ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
            AddFunctionCommand = new RelayCommand(() => AddFunctionRequested?.Invoke(this));

            _moveUpCommand = new RelayCommand(() => { });
            _moveDownCommand = new RelayCommand(() => { });
        }

        /// <summary>
        /// Add a button to this group and wire its RemoveCommand to remove itself.
        /// Always use this method instead of Items.Add() directly.
        /// </summary>
        public void AddItem(QuickActionButtonViewModel btnVm)    // <- NEW
        {
            // Capture to avoid closure over loop variable
            var captured = btnVm;
            captured.RemoveCommand = new RelayCommand(() =>
            {
                Items.Remove(captured);
            });
            Items.Add(captured);
        }

        public List<string> ToActionIds()
            => Items
               .Select(i => i.Action?.Id ?? string.Empty)
               .Where(s => !string.IsNullOrEmpty(s))
               .ToList();
    }
}