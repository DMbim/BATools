using BA.Core.Export.Models;

namespace BA.ViewModels.Export
{
    public class FamilyPickerRowViewModel : BA.UI.Mvvm.ObservableObject
    {
        public FamilyInfo Info { get; }

        public string Name => Info.Name;
        public string CategoryName => Info.CategoryName;
        public bool IsEditable => Info.IsEditable;

        public string StatusLabel => IsEditable ? string.Empty : "Not exportable (system/in-place)";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public FamilyPickerRowViewModel(FamilyInfo info)
        {
            Info = info;
        }
    }
}
