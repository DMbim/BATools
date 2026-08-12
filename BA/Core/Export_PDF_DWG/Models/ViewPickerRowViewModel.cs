using BA.Core.Export.Models;

namespace BA.ViewModels.Export
{
    public class ViewPickerRowViewModel : BA.UI.Mvvm.ObservableObject
    {
        public ViewSummary Info { get; }

        public string Name => Info.Name;
        public string ViewType => Info.ViewType;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public ViewPickerRowViewModel(ViewSummary info, bool isSelected)
        {
            Info = info;
            _isSelected = isSelected;
        }
    }
}