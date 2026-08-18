// FILE: BA_Tools/Warnings/Models/WarningItem.cs
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BA.Warnings.Models
{
    public sealed class WarningItem
    {
        public string Description { get; set; }
        public FailureSeverity Severity { get; set; }
        public FailureDefinitionId FailureDefinitionId { get; set; }
        public List<ElementId> FailingElementIds { get; set; } = new List<ElementId>();
        public List<ElementId> AdditionalElementIds { get; set; } = new List<ElementId>();
        public string ResolutionCaption { get; set; }

        public IEnumerable<ElementId> AllElementIds =>
            FailingElementIds.Concat(AdditionalElementIds).Distinct();
    }
}