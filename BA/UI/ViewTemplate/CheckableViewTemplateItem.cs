using Autodesk.Revit.DB;
using BA.Core.ViewTemplates;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BA.UI.ViewTemplates
{
    public sealed class CheckableViewTemplateItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ElementId Id { get; }
        public string Name { get; }
        public ViewType ViewType { get; }

        public string ViewTypeName => ViewType.ToString();
        public string DisplayName => $"{Name} [{ViewType}]";

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

        public CheckableViewTemplateItem(ViewTemplateItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            Id = item.Id;
            Name = item.Name ?? string.Empty;
            ViewType = item.ViewType;
        }

        public CheckableViewTemplateItem(ElementId id, string name, ViewType viewType)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? string.Empty;
            ViewType = viewType;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}