// File: BA/Commands/CurveToElement/DetailLineSelectionFilter.cs
// Action: CREATE NEW

using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace BA.Commands.CurveToElement
{
    /// <summary>
    /// Restricts selection to DetailCurve elements only. Used with UIDocument.Selection.PickObjects
    /// for the initial curve pick. GetElement(Reference) always returns false - detail curves are
    /// selected by element pick, not by geometric reference pick, and returning true here would
    /// allow face/edge reference selection which this workflow does not use.
    /// </summary>
    public class DetailLineSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is DetailCurve;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}