// FILE: BA_Tools/Warnings/Models/WarningElementDetail.cs
using Autodesk.Revit.DB;

namespace BA.Warnings.Models
{
    public sealed class WarningElementDetail
    {
        public ElementId ElementId { get; set; }
        public string Category { get; set; }
        public string FamilyAndType { get; set; }

        // Value of whatever parameter this specific warning's description names
        // in quotes (e.g. "Mark" or "Type Mark"). Null when the description
        // doesn't name a parameter, or this element doesn't have one by that name.
        public string ErrorValue { get; set; }

        public string DisplayText { get; set; }
    }
}