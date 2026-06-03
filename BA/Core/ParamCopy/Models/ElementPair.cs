using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BATools.ParamCopy.Models
{
    public class ElementPair : ObservableObject
    {
        public ElementId SourceId { get; set; } = ElementId.InvalidElementId;
        public ElementId DestId { get; set; } = ElementId.InvalidElementId;
        public string SourceLabel { get; set; } = string.Empty;
        public string DestLabel { get; set; } = string.Empty;
    }
}
