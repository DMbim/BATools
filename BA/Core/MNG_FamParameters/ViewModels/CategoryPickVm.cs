using Autodesk.Revit.DB;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BA.UI.Parameters
{
    public sealed class CategoryPickVm : INotifyPropertyChanged
    {
        public Category Category { get; }

        public ElementId CategoryId => Category?.Id ?? ElementId.InvalidElementId;

        public string Name => Category?.Name ?? string.Empty;

        private bool _isSelected;
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

        public CategoryPickVm(Category category, bool isSelected = false)
        {
            Category = category ?? throw new ArgumentNullException(nameof(category));
            _isSelected = isSelected;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}