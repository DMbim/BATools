using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BA.UI.KeyplanGrid
{
    public sealed class KeyplanSplitLineItem : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString("N");
        private AxisOrientation _orientation;
        private double _normalized;
        private bool _isEnabled = true;
        private bool _isSelected;
        private string _name = string.Empty;

        public string Id
        {
            get => _id;
            set
            {
                string v = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value;
                if (_id == v) return;
                _id = v;
                OnPropertyChanged();
            }
        }

        public AxisOrientation Orientation
        {
            get => _orientation;
            set
            {
                if (_orientation == value) return;
                _orientation = value;
                OnPropertyChanged();
            }
        }

        public double Normalized
        {
            get => _normalized;
            set
            {
                double v = value;
                if (v < 0.0) v = 0.0;
                if (v > 1.0) v = 1.0;

                if (Math.Abs(_normalized - v) < 1e-12) return;
                _normalized = v;
                OnPropertyChanged();
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                OnPropertyChanged();
            }
        }

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

        public string Name
        {
            get => _name;
            set
            {
                string v = value ?? string.Empty;
                if (_name == v) return;
                _name = v;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}