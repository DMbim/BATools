using Autodesk.Revit.DB;
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

        private bool _isFlipped;
        public bool IsFlipped
        {
            get => _isFlipped;
            set
            {
                if (SetProperty(ref _isFlipped, value))
                    Model.IsFlipped = value;
            }
        }

        private double _offsetMm;
        public double OffsetMm
        {
            get => _offsetMm;
            set
            {
                if (SetProperty(ref _offsetMm, value))
                    Model.OffsetFeet = UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Millimeters);
            }
        }

        public string DisplayLabel => Model.DisplayLabel;
        public string ViewName => Model.ViewName;

        public BA_DimensionCandidateVM(BA_DimensionCandidate model)
        {
            Model = model;
            _isSelected = model.IsSelected;
            _isFlipped = model.IsFlipped;
            _offsetMm = UnitUtils.ConvertFromInternalUnits(model.OffsetFeet, UnitTypeId.Millimeters);
        }
    }
}