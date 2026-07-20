// BA.Core/ParameterPreview.cs
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace BA.Core
{
    public sealed class ParameterPreview : INotifyPropertyChanged
    {
        // ===================== Backing fields =====================

        private string _name = "";
        private string _spec = "";
        private bool _isShared;
        private bool _isBuiltIn;
        private bool _isSelected;

        private string _scope = "Instance";
        private string _originalScope = "Instance";

        private string _groupTypeId = "";
        private string _groupName = "";

        private bool _deleteRequested;
        private string _targetName = "";
        private string _matchedShared = "";
        private double _matchScore;

        private string _formula = "";
        private string _originalFormula = "";
        private bool _canAssignFormula;

        // ===================== Display / identity =====================

        public string Name
        {
            get => _name;
            set { _name = value ?? ""; OnPropertyChanged(); RaiseDecisionChanged(); }
        }

        public string Spec
        {
            get => _spec;
            set { _spec = value ?? ""; OnPropertyChanged(); }
        }

        public bool IsShared
        {
            get => _isShared;
            set { _isShared = value; OnPropertyChanged(); }
        }

        public bool IsBuiltIn
        {
            get => _isBuiltIn;
            set { _isBuiltIn = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        // ===================== Scope =====================

        /// <summary>
        /// Desired scope after apply. Edited by Set Instance / Set Type buttons.
        /// </summary>
        public string Scope
        {
            get => _scope;
            set { _scope = value ?? ""; OnPropertyChanged(); RaiseDecisionChanged(); }
        }

        /// <summary>
        /// Scope as read from the family parameter at load. Set once; never changed.
        /// Used to detect a pending scope change.
        /// </summary>
        public string OriginalScope
        {
            get => _originalScope;
            set { _originalScope = value ?? ""; OnPropertyChanged(); RaiseDecisionChanged(); }
        }

        /// <summary>
        /// True when Scope differs from OriginalScope — a scope-change action is pending.
        /// </summary>
        public bool ScopeChangeNeeded =>
            !string.Equals(_scope, _originalScope, StringComparison.OrdinalIgnoreCase);

        public bool DesiredIsInstance =>
            string.Equals(_scope, "Instance", StringComparison.OrdinalIgnoreCase);

        // ===================== Group =====================

        public string GroupTypeId
        {
            get => _groupTypeId;
            set { _groupTypeId = value ?? ""; OnPropertyChanged(); }
        }

        public string GroupName
        {
            get => _groupName;
            set { _groupName = value ?? ""; OnPropertyChanged(); }
        }

        // ===================== Formula =====================

        /// <summary>
        /// Formula string editable by user. Empty string = clear the formula.
        /// Initialised to OriginalFormula at load time.
        /// </summary>
        public string Formula
        {
            get => _formula;
            set { _formula = value ?? ""; OnPropertyChanged(); RaiseDecisionChanged(); }
        }

        /// <summary>
        /// Formula as read from the family parameter at load. Set once; never changed.
        /// </summary>
        public string OriginalFormula
        {
            get => _originalFormula;
            set { _originalFormula = value ?? ""; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether this parameter supports formula assignment (FamilyParameter.CanAssignFormula).
        /// Set once at load; never changed. Controls formula column editability.
        /// </summary>
        public bool CanAssignFormula
        {
            get => _canAssignFormula;
            set { _canAssignFormula = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// True when the user has changed Formula from its original loaded value.
        /// Uses Ordinal comparison — whitespace differences are treated as changes.
        /// </summary>
        public bool FormulaChanged =>
            !string.Equals(_formula, _originalFormula, StringComparison.Ordinal);

        // ===================== Decision fields =====================

        /// <summary>
        /// Explicit destructive intent. Set via Delete key / Toggle Delete button.
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
        /// System-suggested shared parameter name from the preview scan.
        /// </summary>
        public string MatchedShared
        {
            get => _matchedShared;
            set { _matchedShared = value ?? ""; OnPropertyChanged(); RaiseDecisionChanged(); }
        }

        public double MatchScore
        {
            get => _matchScore;
            set { _matchScore = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// User-editable target name.
        /// Empty => Keep.
        /// Equals MatchedShared => Replace.
        /// Anything else => Rename.
        /// </summary>
        public string TargetName
        {
            get => _targetName;
            set { _targetName = value ?? ""; OnPropertyChanged(); RaiseDecisionChanged(); }
        }

        // ===================== Computed action =====================

        /// <summary>
        /// Primary action used by HarmonizerEventHandler to route execution.
        /// Scope changes and formula changes are orthogonal to this and
        /// are checked separately via ScopeChangeNeeded / FormulaChanged.
        /// </summary>
        public string EffectiveAction
        {
            get
            {
                if (_deleteRequested) return "Delete";

                var target = (_targetName ?? "").Trim();
                if (string.IsNullOrWhiteSpace(target)) return "Keep";

                var matched = (_matchedShared ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(matched) &&
                    target.Equals(matched, StringComparison.OrdinalIgnoreCase))
                    return "Replace";

                if (!target.Equals(_name ?? "", StringComparison.OrdinalIgnoreCase))
                    return "Rename";

                return "Keep";
            }
        }

        /// <summary>
        /// Human-readable display string for the Decision column.
        /// Appends scope and formula change hints when applicable.
        /// </summary>
        public string EffectiveActionDisplay
        {
            get
            {
                var sb = new StringBuilder(EffectiveAction);

                if (ScopeChangeNeeded)
                    sb.Append($" + Scope\u2192{_scope}");

                if (FormulaChanged && _canAssignFormula)
                    sb.Append(" + Formula");

                return sb.ToString();
            }
        }

        // ===================== INotifyPropertyChanged =====================

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propName = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));

        private void RaiseDecisionChanged()
        {
            OnPropertyChanged(nameof(EffectiveAction));
            OnPropertyChanged(nameof(EffectiveActionDisplay));
            OnPropertyChanged(nameof(ScopeChangeNeeded));
            OnPropertyChanged(nameof(FormulaChanged));
        }
    }
}