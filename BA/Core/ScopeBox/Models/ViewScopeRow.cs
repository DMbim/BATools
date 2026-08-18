using Autodesk.Revit.DB;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BA.Core.Views.ScopeBoxes
{
    public sealed class ViewScopeRow : INotifyPropertyChanged
    {
        private bool _isChecked;
        private string _currentScopeBoxName = string.Empty;
        private bool _isLocked;
        private string _status = string.Empty;

        public ElementId ViewId { get; }
        public string ViewName { get; }
        public string ViewTypeName { get; }
        public string FamilyName { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                OnPropertyChanged();
            }
        }

        public string CurrentScopeBoxName
        {
            get => _currentScopeBoxName;
            set
            {
                if (_currentScopeBoxName == value) return;
                _currentScopeBoxName = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                if (_isLocked == value) return;
                _isLocked = value;
                OnPropertyChanged();
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                _status = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public ViewScopeRow(
            ElementId viewId,
            string viewName,
            string viewTypeName,
            string familyName,
            string currentScopeBoxName,
            bool isLocked,
            string status)
        {
            ViewId = viewId;
            ViewName = viewName ?? string.Empty;
            ViewTypeName = viewTypeName ?? string.Empty;
            FamilyName = familyName ?? string.Empty;
            _currentScopeBoxName = currentScopeBoxName ?? string.Empty;
            _isLocked = isLocked;
            _status = status ?? string.Empty;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}