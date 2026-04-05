using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BA.UI.KeyplanGrid
{
    public sealed class AxisPositionItem : INotifyPropertyChanged
    {
        private int _index;
        private double _normalized;

        public int Index
        {
            get => _index;
            set
            {
                if (_index == value) return;
                _index = value;
                OnPropertyChanged();
            }
        }

        public double Normalized
        {
            get => _normalized;
            set
            {
                if (_normalized == value) return;
                _normalized = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}