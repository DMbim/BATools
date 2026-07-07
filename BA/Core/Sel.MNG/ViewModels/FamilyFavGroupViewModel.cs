using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BATools.SelectionManager.Models;

namespace BATools.SelectionManager.ViewModels
{
    public class FamilyFavGroupViewModel : ObservableObject
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

        private bool _isRenaming;
        public bool IsRenaming
        {
            get => _isRenaming;
            set => SetProperty(ref _isRenaming, value);
        }

        public ObservableCollection<FamilyTypeEntryViewModel> Entries { get; } = new();

        // Commands wired internally
        public ICommand ToggleExpandCommand { get; }
        public ICommand BeginRenameCommand { get; }
        public ICommand CommitRenameCommand { get; }
        public ICommand AddFamilyCommand { get; }

        // Commands wired by parent FamiliesToolbarTabViewModel
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

        public event Action<FamilyFavGroupViewModel>? AddFamilyRequested;

        public FamilyFavGroupViewModel(Guid id, string name)
        {
            Id = id;
            _name = name;

            ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
            BeginRenameCommand = new RelayCommand(() => IsRenaming = true);
            CommitRenameCommand = new RelayCommand(() => IsRenaming = false);
            AddFamilyCommand = new RelayCommand(() => AddFamilyRequested?.Invoke(this));

            _moveUpCommand = new RelayCommand(() => { });
            _moveDownCommand = new RelayCommand(() => { });
        }

        /// <summary>
        /// Add an entry and wire its RemoveCommand.
        /// Always use this instead of Entries.Add() directly.
        /// </summary>
        public void AddEntry(FamilyTypeEntryViewModel entry)
        {
            var captured = entry;
            captured.RemoveCommand = new RelayCommand(() =>
            {
                Entries.Remove(captured);
            });
            Entries.Add(captured);
        }

        public List<FamilyTypeEntry> ToModel() =>
            Entries.Select(e => new FamilyTypeEntry
            {
                UniqueId = e.UniqueId,
                FamilyName = e.FamilyName,
                TypeName = e.TypeName,
                CategoryName = e.CategoryName
            }).ToList();
    }
}