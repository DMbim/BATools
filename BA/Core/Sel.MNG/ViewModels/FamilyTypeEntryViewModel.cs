using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BATools.SelectionManager.Infrastructure;

namespace BATools.SelectionManager.ViewModels
{
    public class FamilyTypeEntryViewModel : ObservableObject
    {
        public string UniqueId { get; init; } = string.Empty;
        public string FamilyName { get; init; } = string.Empty;
        public string TypeName { get; init; } = string.Empty;
        public string CategoryName { get; init; } = string.Empty;

        /// <summary>Single-letter icon placeholder — first char of family name.</summary>
        public string IconChar => FamilyName.Length > 0
            ? FamilyName[..1].ToUpperInvariant() : "F";

        private bool _isAvailable = true;
        public bool IsAvailable
        {
            get => _isAvailable;
            set
            {
                SetProperty(ref _isAvailable, value);
                OnPropertyChanged(nameof(UnavailableTooltip));
            }
        }

        public string UnavailableTooltip => IsAvailable
            ? string.Empty
            : $"'{FamilyName} : {TypeName}' not loaded in current document";

        /// <summary>Set externally by FamilyFavGroupViewModel.AddEntry().</summary>
        public ICommand? RemoveCommand { get; set; }

        public ICommand PlaceCommand { get; }

        public FamilyTypeEntryViewModel()
        {
            PlaceCommand = new RelayCommand(ExecutePlace, () => IsAvailable);
        }

        private void ExecutePlace()
        {
            SelectionManagerBridge.Instance.RequestPlaceFamily(
                UniqueId, FamilyName, TypeName);
        }
    }
}