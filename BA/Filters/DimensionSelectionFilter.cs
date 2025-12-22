using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.DB;

namespace BA.Filters
{
    #region Dim Selection Filter
    /// <summary>
    /// Selection filter for multiple commands
    /// </summary>
    public class DimensionSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Dimension;

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
    #endregion
    public sealed class RoomTagSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Autodesk.Revit.DB.Architecture.RoomTag;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
