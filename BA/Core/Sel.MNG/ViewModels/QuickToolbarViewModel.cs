using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BATools.SelectionManager.Actions;
using BATools.SelectionManager.Models;
using BATools.SelectionManager.Services;

namespace BATools.SelectionManager.ViewModels
{
    // ── QuickActionButtonViewModel ────────────────────────────────────────────
    public class QuickActionButtonViewModel : ObservableObject
    {
        private string _label = string.Empty;
        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }

        private string _iconResourceKey = string.Empty;
        public string IconResourceKey
        {
            get => _iconResourceKey;
            set => SetProperty(ref _iconResourceKey, value);
        }

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public ICommand? ExecuteCommand { get; set; }
        public ICommand? RemoveCommand { get; set; }  // <- NEW — wired by ToolbarGroupViewModel.AddItem()
        public IQuickAction? Action { get; set; }

        /// <summary>First character — used as icon placeholder in the group list.</summary>
        public string IconChar => Label.Length > 0 ? Label[..1].ToUpperInvariant() : "?";
    }

    // ── QuickToolbarViewModel ─────────────────────────────────────────────────
    public class QuickToolbarViewModel : ObservableObject
    {
        private readonly IReadOnlyList<IQuickAction> _allActions;
        private readonly Action _showSaveDialog;

        // ── Tabs — object collection holds both ToolbarTabViewModel and SetsToolbarTabViewModel
        public ObservableCollection<object> Tabs { get; } = new();

        private object? _selectedTab;
        public object? SelectedTab
        {
            get => _selectedTab;
            set
            {
                // Deselect previous
                if (_selectedTab is ToolbarTabViewModel prevFn) prevFn.IsSelected = false;
                if (_selectedTab is SetsToolbarTabViewModel prevSt) prevSt.IsSelected = false;
                if (_selectedTab is FamiliesToolbarTabViewModel prevFam) prevFam.IsSelected = false; // <- NEW

                SetProperty(ref _selectedTab, value);

                // Select new
                if (_selectedTab is ToolbarTabViewModel nextFn) nextFn.IsSelected = true;
                if (_selectedTab is SetsToolbarTabViewModel nextSt) nextSt.IsSelected = true;
                if (_selectedTab is FamiliesToolbarTabViewModel nextFam) nextFam.IsSelected = true;
            }
        }

        private bool _isVisible;
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }
        private bool _isFrozen;
        public bool IsFrozen
        {
            get => _isFrozen;
            set => SetProperty(ref _isFrozen, value);
        }

        public ICommand DismissCommand { get; }
        public ICommand SelectTabCommand { get; }
        public ICommand AddTabCommand { get; }
        public ICommand OpenSettingsCommand { get; }

        public QuickToolbarViewModel(Action showSaveDialog)
        {
            _showSaveDialog = showSaveDialog;
            _allActions = QuickActionRegistry.CreateDefault(showSaveDialog);

            DismissCommand = new RelayCommand(() => IsVisible = false);
            SelectTabCommand = new RelayCommand<object>(tab => { if (tab != null) SelectedTab = tab; });
            AddTabCommand = new RelayCommand(ExecuteAddTab);
            OpenSettingsCommand = new RelayCommand(ExecuteOpenSettings);

            LoadProfile();
        }

        public void Activate() => IsVisible = true;
        public void Deactivate() => IsVisible = false;

        // ── Profile loading ───────────────────────────────────────────────────
        private void LoadProfile()
        {
            ToolbarProfile profile = ToolbarProfileService.Load();
            Tabs.Clear();

            foreach (var tabDef in profile.Tabs)
                Tabs.Add(BuildFunctionTab(tabDef));

            if (!Tabs.OfType<ToolbarTabViewModel>().Any())
            {
                foreach (var tabDef in ToolbarProfile.CreateDefault().Tabs)
                    Tabs.Add(BuildFunctionTab(tabDef));
            }

            // Families tab — always present, never persisted in toolbar profile
            var familiesTab = new FamiliesToolbarTabViewModel();  // <- NEW
            Tabs.Add(familiesTab);                                 // <- NEW

            // Sets tab is always last and never persisted
            var setsTab = new SetsToolbarTabViewModel();
            Tabs.Add(setsTab);

            SelectedTab = Tabs.FirstOrDefault();
        }

        private ToolbarTabViewModel BuildFunctionTab(ToolbarTabDefinition tabDef)
        {
            var tabVm = new ToolbarTabViewModel(tabDef.Id, tabDef.Name);

            // Pinned buttons
            foreach (string actionId in tabDef.PinnedActionIds)
            {
                IQuickAction? action =
                    _allActions.FirstOrDefault(a => a.Id == actionId)
                    ?? RevitCommandCatalog.GetActionById(actionId); // <- NEW
                if (action != null)
                    tabVm.PinnedButtons.Add(BuildButtonViewModel(action));
            }

            // Groups — use AddItem() so remove is wired
            foreach (var groupDef in tabDef.Groups)
            {
                var groupVm = new ToolbarGroupViewModel(groupDef.Id, groupDef.Name)
                {
                    IsExpanded = groupDef.IsExpanded
                };

                foreach (string actionId in groupDef.ActionIds)
                {
                    // Try plugin actions first, then Revit catalog  // <- NEW
                    IQuickAction? action =
                        _allActions.FirstOrDefault(a => a.Id == actionId)
                        ?? RevitCommandCatalog.GetActionById(actionId); // <- NEW
                    if (action != null)
                        groupVm.AddItem(BuildButtonViewModel(action));
                }

                tabVm.WireGroupCommands(groupVm);
                tabVm.Groups.Add(groupVm);
            }

            tabVm.AddToPinnedCommand = new RelayCommand(
                () => ShowAddActionDialog(tabVm, null));

            tabVm.AddFunctionToGroupRequested +=
                grp => ShowAddActionDialog(null, grp);

            return tabVm;
        }

        public QuickActionButtonViewModel BuildButtonViewModel(IQuickAction action)
        {
            var btnVm = new QuickActionButtonViewModel
            {
                Label = action.DefaultLabel,
                IconResourceKey = action.IconResourceKey,
                IsEnabled = true,
                Action = action
            };

            btnVm.ExecuteCommand = new RelayCommand(() =>
            {
                Services.RecentActionsService.Record(action.Id); // <- NEW
                action.Execute(new List<ElementId>());
                IsVisible = false;
            });

            // RemoveCommand will be set by ToolbarGroupViewModel.AddItem()
            // or left null for pinned buttons (they use RemovePinnedCommand on the tab)

            return btnVm;
        }

        // ── Add function dialog ────────────────────────────────────────────────
        private void ShowAddActionDialog(ToolbarTabViewModel? targetTab,
                                         ToolbarGroupViewModel? targetGroup)
        {
            var dialog = new Views.AddFunctionDialog(_allActions.ToList());
            if (dialog.ShowDialog() != true || dialog.SelectedAction == null)
                return;

            var btnVm = BuildButtonViewModel(dialog.SelectedAction);

            if (targetTab != null)
                targetTab.PinnedButtons.Add(btnVm);
            else
                targetGroup?.AddItem(btnVm);  // <- uses AddItem() so remove is wired

            SaveProfile();
        }

        // ── Tab management ────────────────────────────────────────────────────
        private void ExecuteAddTab()
        {
            var tabDef = new ToolbarTabDefinition { Name = $"Tab {Tabs.OfType<ToolbarTabViewModel>().Count() + 1}" };
            ToolbarTabViewModel tabVm = BuildFunctionTab(tabDef);

            // Insert before the Families tab
            int insertBefore = Tabs.IndexOf(
                Tabs.OfType<FamiliesToolbarTabViewModel>().First()); // <- CHANGED
            Tabs.Insert(insertBefore, tabVm);

            SelectedTab = tabVm;
            SaveProfile();
        }

        // ── Persistence — only serializes ToolbarTabViewModel, skips Sets tab ─
        public void SaveProfile()
        {
            var profile = new ToolbarProfile { ProfileName = "Default" };

            foreach (var tab in Tabs.OfType<ToolbarTabViewModel>())
            {
                profile.Tabs.Add(new ToolbarTabDefinition
                {
                    Id = tab.Id,
                    Name = tab.Name,
                    PinnedActionIds = tab.GetPinnedActionIds(),
                    Groups = tab.Groups.Select(g => new ToolbarGroupDefinition
                    {
                        Id = g.Id,
                        Name = g.Name,
                        IsExpanded = g.IsExpanded,
                        ActionIds = g.ToActionIds()
                    }).ToList()
                });
            }

            ToolbarProfileService.Save(profile);
        }
        private void ExecuteOpenSettings()
        {
            var settingsVm = new ToolbarSettingsViewModel();
            var window = new Views.ToolbarSettingsWindow(settingsVm);
            window.ShowDialog();
        }
    }
}