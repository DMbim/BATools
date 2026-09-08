// FILE: BA_Tools/Warnings/Models/WarningItem.cs
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        public int ClassifiedSeverity { get; set; } = 0;
        public string Category { get; set; } = "Unclassified";

        // Per-element breakdown, loaded lazily on first UI expand rather than
        // eagerly on every refresh, given the scaling cost of resolving every
        // element on every debounced live-refresh. An ObservableCollection so
        // the nested ItemsControl updates in place without WarningItem needing
        // full INotifyPropertyChanged support.
        public ObservableCollection<WarningElementDetail> ElementDetails { get; } = new ObservableCollection<WarningElementDetail>();
        public bool DetailsLoaded { get; set; } = false;

        public IEnumerable<ElementId> AllElementIds =>
            FailingElementIds.Concat(AdditionalElementIds).Distinct();
    }
}