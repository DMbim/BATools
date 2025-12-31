using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BA.Core
{
    public class ParameterPreview : INotifyPropertyChanged
    {
        private string _name;
        private bool _isInstance;
        private bool _isShared;
        private bool _isBuiltIn;
        private string _spec;
        private string _action;
        private string _newName;
        private string _matchedShared;
        private double _matchScore;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public bool IsInstance
        {
            get => _isInstance;
            set { _isInstance = value; OnPropertyChanged(); }
        }

        public bool IsShared
        {
            get => _isShared;
            set { _isShared = value; OnPropertyChanged(); }
        }

        /// <summary>True if this maps to a built-in parameter (Width, Height, etc).</summary>
        public bool IsBuiltIn
        {
            get => _isBuiltIn;
            set { _isBuiltIn = value; OnPropertyChanged(); }
        }

        public string Spec
        {
            get => _spec;
            set { _spec = value; OnPropertyChanged(); }
        }

        /// <summary>Keep / Replace / Rename</summary>
        public string Action
        {
            get => _action;
            set { _action = value; OnPropertyChanged(); }
        }

        public string NewName
        {
            get => _newName;
            set { _newName = value; OnPropertyChanged(); }
        }

        public string MatchedShared
        {
            get => _matchedShared;
            set { _matchedShared = value; OnPropertyChanged(); }
        }

        public double MatchScore
        {
            get => _matchScore;
            set { _matchScore = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
