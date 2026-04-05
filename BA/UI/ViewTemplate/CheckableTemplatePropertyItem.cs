using Autodesk.Revit.DB;
using BA.Core.ViewTemplates;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BA.UI.ViewTemplates
{
    public sealed class CheckableTemplatePropertyItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ElementId ParameterId { get; }
        public string Name { get; }

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

        public CheckableTemplatePropertyItem(TemplatePropertyItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            ParameterId = item.ParameterId;
            Name = item.Name ?? string.Empty;
            _isSelected = item.IsSelected;
        }

        public CheckableTemplatePropertyItem(ElementId parameterId, string name, bool isSelected = false)
        {
            ParameterId = parameterId ?? throw new ArgumentNullException(nameof(parameterId));
            Name = name ?? string.Empty;
            _isSelected = isSelected;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}