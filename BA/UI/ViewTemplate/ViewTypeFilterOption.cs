using Autodesk.Revit.DB;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BA.UI.ViewTemplates
{
    /// <summary>
    /// One checkable entry in the Source template view type filter list.
    /// Multiple can be checked at once; none checked means no view type filtering.
    /// </summary>
    public sealed class CheckableViewTypeItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ViewType ViewType { get; }
        public string DisplayName { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public CheckableViewTypeItem(ViewType viewType, string displayName)
        {
            ViewType = viewType;
            DisplayName = displayName ?? string.Empty;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}