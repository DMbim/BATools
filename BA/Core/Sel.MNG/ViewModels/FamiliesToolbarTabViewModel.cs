using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BATools.SelectionManager.Infrastructure;
using BATools.SelectionManager.Models;
using BATools.SelectionManager.Services;
using BATools.SelectionManager.Views;

namespace BATools.SelectionManager.ViewModels
{
    public class FamiliesToolbarTabViewModel : ObservableObject
    {
        public Guid Id { get; } = new Guid("FA000000-0000-0000-0000-000000000001");
        public string Name { get; } = "Families";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                SetProperty(ref _isSelected, value);
                if (value) RefreshAvailability();
            }
        }

        public ObservableCollection<FamilyFavGroupViewModel> Groups { get; } = new();

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand AddGroupCommand { get; }

        public FamiliesToolbarTabViewModel()
        {
            AddGroupCommand = new RelayCommand(ExecuteAddGroup);
            LoadProfile();
        }

        // ── Profile ───────────────────────────────────────────────────────────

        private void LoadProfile()
        {
            var profile = FavoriteFamiliesService.Load();
            Groups.Clear();

            foreach (var groupDef in profile.Groups)
            {
                var groupVm = BuildGroupViewModel(groupDef);
                Groups.Add(groupVm);
            }

            UpdateStatus();
        }

        private FamilyFavGroupViewModel BuildGroupViewModel(
            FamilyFavGroupDefinition groupDef)
        {
            var vm = new FamilyFavGroupViewModel(groupDef.Id, groupDef.Name)
            {
                IsExpanded = groupDef.IsExpanded
            };

            foreach (var entry in groupDef.Entries)
                vm.AddEntry(BuildEntryViewModel(entry));

            WireGroupCommands(vm);
            return vm;
        }

        private FamilyTypeEntryViewModel BuildEntryViewModel(FamilyTypeEntry entry) =>
            new FamilyTypeEntryViewModel
            {
                UniqueId = entry.UniqueId,
                FamilyName = entry.FamilyName,
                TypeName = entry.TypeName,
                CategoryName = entry.CategoryName
            };

        private void WireGroupCommands(FamilyFavGroupViewModel vm)
        {
            vm.MoveUpCommand = new RelayCommand(
                () => MoveGroup(vm, -1),
                () => Groups.IndexOf(vm) > 0);

            vm.MoveDownCommand = new RelayCommand(
                () => MoveGroup(vm, 1),
                () => Groups.IndexOf(vm) < Groups.Count - 1);

            vm.AddFamilyRequested += grp => ShowPickDialog(grp);

            // Save whenever an entry is removed
            vm.Entries.CollectionChanged += (_, _) => SaveProfile();
        }

        private void MoveGroup(FamilyFavGroupViewModel vm, int delta)
        {
            int idx = Groups.IndexOf(vm);
            int newIdx = idx + delta;
            if (newIdx < 0 || newIdx >= Groups.Count) return;
            Groups.Move(idx, newIdx);
            SaveProfile();
        }

        private void ExecuteAddGroup()
        {
            var groupDef = new FamilyFavGroupDefinition { Name = "New Group" };
            var vm = BuildGroupViewModel(groupDef);
            Groups.Add(vm);
            SaveProfile();
            // Start rename immediately
            vm.IsRenaming = true;
        }

        // ── Pick dialog ───────────────────────────────────────────────────────

        private void ShowPickDialog(FamilyFavGroupViewModel targetGroup)
        {
            var dialog = new PickFamilyTypeDialog();
            dialog.Show();

            // Load families from Revit into dialog
            SelectionManagerBridge.Instance.RequestReadFamilyTypes(families =>
            {
                dialog.PopulateList(families);
            });

            dialog.FamilySelected += entry =>
            {
                // Avoid duplicates within the same group
                if (targetGroup.Entries.Any(e =>
                    e.FamilyName == entry.FamilyName &&
                    e.TypeName == entry.TypeName))
                    return;

                targetGroup.AddEntry(BuildEntryViewModel(new FamilyTypeEntry
                {
                    UniqueId = entry.UniqueId,
                    FamilyName = entry.FamilyName,
                    TypeName = entry.TypeName,
                    CategoryName = entry.CategoryName
                }));

                SaveProfile();
                UpdateStatus();
            };
        }

        // ── Availability refresh ──────────────────────────────────────────────

        private void RefreshAvailability()
        {
            // Re-read family types to update IsAvailable flags
            SelectionManagerBridge.Instance.RequestReadFamilyTypes(families =>
            {
                var availableSet = families
                    .Select(f => (f.FamilyName, f.TypeName))
                    .ToHashSet();

                foreach (var group in Groups)
                    foreach (var entry in group.Entries)
                        entry.IsAvailable = availableSet.Contains(
                            (entry.FamilyName, entry.TypeName));

                UpdateStatus();
            });
        }

        // ── Persistence ───────────────────────────────────────────────────────

        public void SaveProfile()
        {
            var profile = new FavoriteFamiliesProfile
            {
                Groups = Groups.Select(g => new FamilyFavGroupDefinition
                {
                    Id = g.Id,
                    Name = g.Name,
                    IsExpanded = g.IsExpanded,
                    Entries = g.ToModel()
                }).ToList()
            };
            FavoriteFamiliesService.Save(profile);
        }

        private void UpdateStatus()
        {
            int total = Groups.Sum(g => g.Entries.Count);
            StatusMessage = total == 0 ? "No families added" : $"{Groups.Count} group(s), {total} type(s)";
        }
    }
}