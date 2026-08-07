using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BATools.ParamCopy.Models
{
    /// <summary>
    /// A single selectable entry in a Display Params multi-select popup.
    /// Name is immutable once constructed; only IsSelected changes.
    /// </summary>
    public class CheckableParam : ObservableObject
    {
        public string Name { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                    SelectionChanged?.Invoke();
            }
        }

        /// <summary>
        /// Fired whenever IsSelected actually changes, so the owning
        /// ViewModel can recompute a summary string without polling.
        /// </summary>
        public event Action? SelectionChanged;

        public CheckableParam(string name, bool isSelected)
        {
            Name = name;
            _isSelected = isSelected;
        }
    }
}