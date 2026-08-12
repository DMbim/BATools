using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace BA.Core.Content.Models
{
    /// <summary>
    /// Tree leaf representing a single loaded FamilySymbol (Type).
    /// Implements INotifyPropertyChanged directly rather than inheriting
    /// BA.UI.Mvvm.ObservableObject, because that base class's member
    /// signatures were not available to verify against. Swap to the real
    /// base class once confirmed if desired.
    /// </summary>
    public sealed class LoadedTypeNode : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private int _instanceCount;
        private bool _isUnused;
        private bool _isFavorite;
        private bool _isChecked;
        private string _previewPath = string.Empty;
        private List<string> _tags = new();
        private bool _isVisible = true;
        public ElementId TypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId FamilyId { get; set; } = ElementId.InvalidElementId;
        public string ParentFamilyName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public int InstanceCount
        {
            get => _instanceCount;
            set
            {
                if (SetField(ref _instanceCount, value))
                {
                    IsUnused = value == 0;
                }
            }
        }

        public bool IsUnused
        {
            get => _isUnused;
            private set => SetField(ref _isUnused, value);
        }

        public bool IsFavorite
        {
            get => _isFavorite;
            set => SetField(ref _isFavorite, value);
        }

        public bool IsChecked
        {
            get => _isChecked;
            set => SetField(ref _isChecked, value);
        }
        public bool IsVisible
        {
            get => _isVisible;
            set => SetField(ref _isVisible, value);
        }
        public string PreviewPath
        {
            get => _previewPath;
            set => SetField(ref _previewPath, value);
        }

        public List<string> Tags
        {
            get => _tags;
            set => SetField(ref _tags, value);
        }

        public string DisplayText => InstanceCount == 0
            ? $"{Name} (unused)"
            : $"{Name} ({InstanceCount})";

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}