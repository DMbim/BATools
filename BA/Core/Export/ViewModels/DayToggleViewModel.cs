using System;
using BA.UI.Mvvm;

namespace BA.ViewModels.Export
{
    public class DayToggleViewModel : BA.UI.Mvvm.ObservableObject
    {
        private bool _isSelected;

        public DayOfWeek Day { get; }
        public string Label { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public DayToggleViewModel(DayOfWeek day)
        {
            Day = day;
            Label = day.ToString().Substring(0, 3);
        }
    }
}
