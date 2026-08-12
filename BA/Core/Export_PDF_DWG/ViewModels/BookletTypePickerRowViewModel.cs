using BA.Core.Export.Models;

namespace BA.ViewModels.Export
{
    public class BookletTypePickerRowViewModel : BA.UI.Mvvm.ObservableObject
    {
        public BookletTypeInfo Info { get; }

        public string FamilyName => Info.FamilyName;
        public string TypeName => Info.TypeName;
        public string CategoryName => Info.CategoryName;
        public bool HasPlacedInstance => Info.HasPlacedInstance;

        private bool _isInstanceRequired = true;

        /// <summary>
        /// True for Real Views mode, which needs an actual instance to cut
        /// an elevation and floor plan through. False for Legend
        /// Components mode, which references the type symbolically and
        /// needs no placed instance at all. Set by the owning ViewModel
        /// whenever generation mode changes, not fixed at row creation.
        /// </summary>
        public bool IsInstanceRequired
        {
            get => _isInstanceRequired;
            set
            {
                if (SetProperty(ref _isInstanceRequired, value))
                {
                    OnPropertyChanged(nameof(StatusLabel));
                    OnPropertyChanged(nameof(IsSelectable));
                }
            }
        }

        public bool IsSelectable => HasPlacedInstance || !IsInstanceRequired;

        public string StatusLabel => IsSelectable
            ? string.Empty
            : "No placed instance, cannot generate a booklet for this type";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public BookletTypePickerRowViewModel(BookletTypeInfo info)
        {
            Info = info;
        }
    }
}
