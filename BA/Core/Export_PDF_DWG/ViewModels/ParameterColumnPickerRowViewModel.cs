using BA.Core.Export.Models;
using BA.UI.Mvvm;

namespace BA.ViewModels.Export
{
    public class ParameterColumnPickerRowViewModel : BA.UI.Mvvm.ObservableObject
    {
        public ParameterColumnCandidate Candidate { get; }

        public string DisplayName => Candidate.DisplayName;
        public string SourceLabel => Candidate.Source.ToString();
        public string InstanceOrType => Candidate.IsInstance ? "Instance" : "Type";
        public string DataTypeLabel => Candidate.ValueKind.ToString();

        public string OccurrenceLabel
        {
            get
            {
                switch (Candidate.Occurrence)
                {
                    case ParameterColumnOccurrence.All:
                        return "All sheets";
                    case ParameterColumnOccurrence.Some:
                        return "Some sheets";
                    default:
                        return "One sheet";
                }
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool IsAlreadyAdded { get; }

        public ParameterColumnPickerRowViewModel(ParameterColumnCandidate candidate, bool isAlreadyAdded)
        {
            Candidate = candidate;
            IsAlreadyAdded = isAlreadyAdded;
        }
    }
}
