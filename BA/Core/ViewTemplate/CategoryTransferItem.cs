using Autodesk.Revit.DB;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BA.Core.ViewTemplates
{
    public sealed class CategoryTransferItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ElementId CategoryId { get; }
        public string Name { get; }
        public CategoryType CategoryType { get; }

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

        public CategoryTransferItem(ElementId categoryId, string name, CategoryType categoryType, bool isSelected = false)
        {
            CategoryId = categoryId ?? throw new ArgumentNullException(nameof(categoryId));
            Name = name ?? string.Empty;
            CategoryType = categoryType;
            _isSelected = isSelected;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}