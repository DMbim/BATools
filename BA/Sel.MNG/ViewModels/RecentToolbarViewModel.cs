using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BATools.SelectionManager.Actions;
using BATools.SelectionManager.Services;

namespace BATools.SelectionManager.ViewModels
{
    public class RecentToolbarViewModel : ObservableObject
    {
        private readonly IReadOnlyList<IQuickAction> _allActions;

        public ObservableCollection<QuickActionButtonViewModel> Buttons { get; } = new();

        public bool IsEmpty => Buttons.Count == 0;

        private bool _isVisible;
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public ICommand DismissCommand { get; }

        public RecentToolbarViewModel(IReadOnlyList<IQuickAction> allActions)
        {
            _allActions = allActions;
            DismissCommand = new RelayCommand(() => IsVisible = false);
            Buttons.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        }

        /// <summary>
        /// Rebuilds the button list from persisted recents, then sets IsVisible.
        /// The window must not open if the recents list is empty.
        /// </summary>
        public void RefreshAndShow()
        {
            Buttons.Clear();

            var recents = RecentActionsService.GetRecents();

            foreach (string actionId in recents)
            {
                IQuickAction? action =
                    _allActions.FirstOrDefault(a => a.Id == actionId)
                    ?? RevitCommandCatalog.GetActionById(actionId);

                if (action == null) continue;

                var btnVm = new QuickActionButtonViewModel
                {
                    Label = action.DefaultLabel,
                    IconResourceKey = action.IconResourceKey,
                    IsEnabled = true,
                    Action = action
                };

                // Capture locally — closure over loop variable is a bug.
                var capturedAction = action;

                btnVm.ExecuteCommand = new RelayCommand(() =>
                {
                    RecentActionsService.Record(capturedAction.Id);
                    capturedAction.Execute(new List<ElementId>());
                    IsVisible = false;
                });

                Buttons.Add(btnVm);
            }

            IsVisible = Buttons.Count > 0;
        }

        public void Deactivate() => IsVisible = false;
    }
}