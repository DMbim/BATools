using Autodesk.Revit.UI.Selection;

namespace BA.Commands.Rooms
{
    internal class RoomSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            throw new NotImplementedException();
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            throw new NotImplementedException();
        }
    }
}