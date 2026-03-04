using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BA.Core
{
    public class ParameterPreview : INotifyPropertyChanged
    {
        private string _name;
        private string _spec;
        private bool _isShared;
        private bool _isBuiltIn;
        private bool _isSelected;

        // "Instance" / "Type"
        private string _scope;

        // group stored as TypeId string for stable WPF selection
        private string _groupTypeId;
        private string _groupName;

        private string _action;
        private string _newName;
        private string _matchedShared;
        private double _matchScore;

        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        public string Spec { get => _spec; set { _spec = value; OnPropertyChanged(); } }
        public bool IsShared { get => _isShared; set { _isShared = value; OnPropertyChanged(); } }
        public bool IsBuiltIn { get => _isBuiltIn; set { _isBuiltIn = value; OnPropertyChanged(); } }
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

        public string Scope
        {
            get => _scope;
            set { _scope = value; OnPropertyChanged(); }
        }

        /// <summary>ForgeTypeId.TypeId string</summary>
        public string GroupTypeId
        {
            get => _groupTypeId;
            set { _groupTypeId = value; OnPropertyChanged(); }
        }

        /// <summary>Human label for current group</summary>
        public string GroupName
        {
            get => _groupName;
            set { _groupName = value; OnPropertyChanged(); }
        }

        /// <summary>Keep / Replace / Rename / Delete</summary>
        public string Action
        {
            get => _action;
            set { _action = value; OnPropertyChanged(); }
        }

        private bool _suppressAutoAction;

        public string NewName
        {
            get => _newName;
            set
            {
                _newName = value;
                OnPropertyChanged();

                if (_suppressAutoAction) return;

                if (string.Equals(Action, "Replace", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Action, "Delete", StringComparison.OrdinalIgnoreCase))
                    return;

                if (string.IsNullOrWhiteSpace(_newName) ||
                    _newName.Equals(Name, StringComparison.OrdinalIgnoreCase))
                    Action = "Keep";
                else
                    Action = "Rename";
            }
        }

        public string MatchedShared { get => _matchedShared; set { _matchedShared = value; OnPropertyChanged(); } }
        public double MatchScore { get => _matchScore; set { _matchScore = value; OnPropertyChanged(); } }

        public bool DesiredIsInstance => string.Equals(Scope, "Instance", StringComparison.OrdinalIgnoreCase);

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}