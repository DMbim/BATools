using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace BA.Core.Content.Models
{
    public sealed class LoadedFamilyNode : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private bool _isUnused;
        private bool _isChecked;
        private bool _isVisible = true;

        public ElementId FamilyId { get; set; } = ElementId.InvalidElementId;
        public string CategoryName { get; set; } = string.Empty;

        public ObservableCollection<LoadedTypeNode> Types { get; } = new();

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public bool IsUnused
        {
            get => _isUnused;
            private set => SetField(ref _isUnused, value);
        }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (SetField(ref _isChecked, value))
                {
                    foreach (var type in Types)
                        type.IsChecked = value;
                }
            }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetField(ref _isVisible, value);
        }

        public string DisplayText => $"{Name} ({Types.Count} type{(Types.Count == 1 ? "" : "s")})";

        public void RecomputeUnusedState()
        {
            IsUnused = Types.Count > 0 && Types.All(t => t.IsUnused);
        }

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