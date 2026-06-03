using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BATools.ParamCopy.Models
{
    public class ElementListItem : ObservableObject
    {
        public ElementId ElementId { get; init; } = ElementId.InvalidElementId;
        public string Category { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Resolved values for each display parameter name.
        /// Key = parameter name, Value = resolved string value.
        /// </summary>
        public Dictionary<string, string> ParameterValues { get; init; } = new();

        /// <summary>
        /// Returns the value for the given parameter name, or empty string.
        /// Used by dynamically generated DataGrid columns.
        /// </summary>
        public string GetParameterValue(string paramName)
        {
            return ParameterValues.TryGetValue(paramName, out var val) ? val : string.Empty;
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
