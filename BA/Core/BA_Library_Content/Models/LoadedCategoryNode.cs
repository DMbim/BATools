using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace BA.Core.Content.Models
{
    public sealed class LoadedCategoryNode : INotifyPropertyChanged
    {
        private bool _isChecked;
        private bool _isVisible = true;

        public string Name { get; set; } = string.Empty;
        public ElementId CategoryId { get; set; } = ElementId.InvalidElementId;

        public ObservableCollection<LoadedFamilyNode> Families { get; } = new();

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (SetField(ref _isChecked, value))
                {
                    foreach (var family in Families)
                        family.IsChecked = value;
                }
            }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetField(ref _isVisible, value);
        }

        public string DisplayText => $"{Name} ({Families.Count})";

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