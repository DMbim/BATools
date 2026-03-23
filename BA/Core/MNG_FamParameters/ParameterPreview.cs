using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BA.Core
{
    public sealed class ParameterPreview : INotifyPropertyChanged
    {
        private string _name = "";
        private string _spec = "";
        private bool _isShared;
        private bool _isBuiltIn;
        private bool _isSelected;

        // "Instance" / "Type"
        private string _scope = "Instance";

        // group stored as TypeId string for stable WPF selection
        private string _groupTypeId = "";
        private string _groupName = "";

        // User decision inputs
        private bool _deleteRequested;

        // "Target" is what user wants to end up with:
        // - empty => Keep
        // - equals MatchedShared => Replace
        // - anything else => Rename (or Replace if you decide later)
        private string _targetName = "";

        // Suggested shared param name (system)
        private string _matchedShared = "";
        private double _matchScore;

        public string Name { get => _name; set { _name = value ?? ""; OnPropertyChanged(); RaiseDecisionChanged(); } }
        public string Spec { get => _spec; set { _spec = value ?? ""; OnPropertyChanged(); } }
        public bool IsShared { get => _isShared; set { _isShared = value; OnPropertyChanged(); } }
        public bool IsBuiltIn { get => _isBuiltIn; set { _isBuiltIn = value; OnPropertyChanged(); } }
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

        public string Scope { get => _scope; set { _scope = value ?? ""; OnPropertyChanged(); } }

        public string GroupTypeId { get => _groupTypeId; set { _groupTypeId = value ?? ""; OnPropertyChanged(); } }
        public string GroupName { get => _groupName; set { _groupName = value ?? ""; OnPropertyChanged(); } }

        /// <summary>
        /// Explicit destructive intent.
        /// UI sets this via Delete key / "Toggle Delete" button.
        /// </summary>
        public bool DeleteRequested
        {
            get => _deleteRequested;
            set
            {
                if (_deleteRequested == value) return;
                _deleteRequested = value;
                OnPropertyChanged();
                RaiseDecisionChanged();
            }
        }

        /// <summary>
        /// Suggested shared parameter match (system).
        /// </summary>
        public string MatchedShared
        {
            get => _matchedShared;
            set
            {
                _matchedShared = value ?? "";
                OnPropertyChanged();
                RaiseDecisionChanged();
            }
        }

        public double MatchScore
        {
            get => _matchScore;
            set { _matchScore = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// User-editable field:
        /// - if left as suggested (MatchedShared): Replace
        /// - if user types different: Rename
        /// - if empty: Keep
        /// </summary>
        public string TargetName
        {
            get => _targetName;
            set
            {
                _targetName = value ?? "";
                OnPropertyChanged();
                RaiseDecisionChanged();
            }
        }

        public bool DesiredIsInstance => string.Equals(Scope, "Instance", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Derived action used by the backend.
        /// This replaces the old Action string.
        /// </summary>
        public string EffectiveAction
        {
            get
            {
                if (DeleteRequested) return "Delete";

                var target = (TargetName ?? "").Trim();
                if (string.IsNullOrWhiteSpace(target)) return "Keep";

                var matched = (MatchedShared ?? "").Trim();

                // If system suggested a shared param and user kept it: Replace
                if (!string.IsNullOrWhiteSpace(matched) &&
                    target.Equals(matched, StringComparison.OrdinalIgnoreCase))
                    return "Replace";

                // If user typed something:
                if (!target.Equals(Name ?? "", StringComparison.OrdinalIgnoreCase))
                    return "Rename";

                // target equals original name: Keep
                return "Keep";
            }
        }

        /// <summary>
        /// Convenience for UI display.
        /// </summary>
        public string EffectiveActionDisplay => EffectiveAction;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propName = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));

        private void RaiseDecisionChanged()
        {
            OnPropertyChanged(nameof(EffectiveAction));
            OnPropertyChanged(nameof(EffectiveActionDisplay));
        }
    }
}