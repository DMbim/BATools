// File: BA/Commands/CurveToElement/DetailLineSelectionFilter.cs
// Action: REPLACE (full file)

using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace BA.Commands.CurveToElement
{
    /// <summary>
    /// Restricts selection to curve-based elements this tool can process: DetailCurve (detail
    /// lines, view-specific) and ModelCurve (model lines, 3D, includes ModelLine, ModelArc,
    /// ModelEllipse, ModelNurbSpline and ModelHermiteSpline, since all derive from ModelCurve).
    /// Both DetailCurve and ModelCurve derive from the CurveElement base class, so the
    /// classification/chaining/generation pipeline downstream treats them uniformly via
    /// CurveElement.LineStyle and CurveElement.GeometryCurve - see
    /// DetailLineClassificationService. This lets one PickObjects call return a mix of both.
    ///
    /// Class name is retained even though it now also covers model lines, to avoid touching
    /// call sites beyond CurveToElementCommand.cs sight unseen. Rename to
    /// CurveElementSelectionFilter if you want the name to match scope.
    ///
    /// GetElement(Reference) always returns false - these elements are selected by element pick,
    /// not by geometric reference pick, and returning true here would allow face/edge reference
    /// selection which this workflow does not use.
    /// </summary>
    public class DetailLineSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is DetailCurve || elem is ModelCurve; // <- CHANGED (was DetailCurve only)
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}