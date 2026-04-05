using Autodesk.Revit.DB;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BA.Core.ViewTemplates
{
    public sealed class ViewFilterTransferItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ElementId FilterId { get; }
        public string Name { get; }
        public string FilterTypeName { get; }

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

        public ViewFilterTransferItem(ElementId filterId, string name, string filterTypeName, bool isSelected = false)
        {
            FilterId = filterId ?? throw new ArgumentNullException(nameof(filterId));
            Name = name ?? string.Empty;
            FilterTypeName = filterTypeName ?? string.Empty;
            _isSelected = isSelected;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}