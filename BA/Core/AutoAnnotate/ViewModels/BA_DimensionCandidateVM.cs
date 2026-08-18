using BA.UI.Mvvm;
using BA.BIM.Core.Dimensioning.Models;

namespace BA.BIM.Commands.Dimension
{
    public sealed class BA_DimensionCandidateVM : BA.UI.Mvvm.ObservableObject
    {
        public BA_DimensionCandidate Model { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                    Model.IsSelected = value;
            }
        }

        public string DisplayLabel => Model.DisplayLabel;
        public string ViewName => Model.ViewName;

        public BA_DimensionCandidateVM(BA_DimensionCandidate model)
        {
            Model = model;
            _isSelected = model.IsSelected;
        }
    }
}