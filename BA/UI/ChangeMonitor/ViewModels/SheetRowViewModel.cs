using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BA.UI.ViewModels
{
    public sealed class SheetRowViewModel : INotifyPropertyChanged
    {
        private bool _updateDate;
        private bool _updateRevision;
        private bool _updateBoth;

        public string SheetNumber { get; set; } = "";
        public string SheetName { get; set; } = "";
        public string IssueDate { get; set; } = "";
        public string CurrentRevision { get; set; } = "";

        public bool UpdateDate
        {
            get => _updateDate;
            set
            {
                if (_updateDate == value) return;
                _updateDate = value;
                if (value) UpdateBoth = false; // mutually exclusive
                OnPropertyChanged();
            }
        }

        public bool UpdateRevision
        {
            get => _updateRevision;
            set
            {
                if (_updateRevision == value) return;
                _updateRevision = value;
                if (value) UpdateBoth = false; // mutually exclusive
                OnPropertyChanged();
            }
        }

        public bool UpdateBoth
        {
            get => _updateBoth;
            set
            {
                if (_updateBoth == value) return;
                _updateBoth = value;

                if (value)
                {
                    // make "both" explicit and avoid ambiguous combos
                    _updateDate = false;
                    _updateRevision = false;
                    OnPropertyChanged(nameof(UpdateDate));
                    OnPropertyChanged(nameof(UpdateRevision));
                }

                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
