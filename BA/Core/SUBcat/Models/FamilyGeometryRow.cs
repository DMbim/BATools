using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BA.Subcategories.Models
{
    public class FamilyGeometryRow : ObservableObject
    {
        public ElementId Id { get; init; } = ElementId.InvalidElementId;
        public string DisplayName { get; init; } = string.Empty;
        public string CategoryName { get; init; } = string.Empty;

        private string _subcategoryName = string.Empty;
        public string SubcategoryName
        {
            get => _subcategoryName;
            set => SetProperty(ref _subcategoryName, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool HasSubcategory => !string.IsNullOrWhiteSpace(SubcategoryName);
    }
}
