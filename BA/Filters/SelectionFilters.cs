using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI.Selection;

namespace BA.Filters
{
    /// <summary>
    /// Select only Dimension elements.
    /// </summary>
    public sealed class DimensionSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Dimension;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }

    /// <summary>
    /// Select only RoomTag elements.
    /// </summary>
    public sealed class RoomTagSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is RoomTag;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }

    /// <summary>
    /// Select Rooms or RoomTags (useful for tools that accept either).
    /// </summary>
    public sealed class RoomOrRoomTagSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Room || elem is RoomTag;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}