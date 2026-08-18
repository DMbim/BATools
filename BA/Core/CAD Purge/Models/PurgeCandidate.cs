// File: BA_Tools/CadPurge/Models/PurgeCandidate.cs
using System;
using Autodesk.Revit.DB;

namespace BA.CadPurge.Models
{
    /// <summary>
    /// A single scanned non-standard element (imported line pattern or text style) eligible for
    /// bulk deletion or mapping to a corporate-standard equivalent.
    ///
    /// NOT used for DWG imports/links — those are report-only per the current scope (the Revit
    /// API exposes no supported way to explode an ImportInstance outside the UI-bound Explode
    /// command) and are represented by <see cref="DwgImportReportEntry"/> instead.
    /// </summary>
    public sealed class PurgeCandidate
    {
        /// <summary>Id of the source LinePatternElement or TextNoteType in the active document.</summary>
        public ElementId ElementId { get; }

        public PurgeItemType ItemType { get; }

        /// <summary>Element.Name as read at scan time. Not re-read after mutation — treat as a snapshot.</summary>
        public string Name { get; }

        /// <summary>
        /// Number of elements in the model that reference this type at scan time.
        /// For LinePattern: informational only — mapping is an in-place SetLinePattern(), no
        /// element reassignment needed, so this never blocks a map action.
        /// For TextStyle: the number of TextNote elements that will need ChangeTypeId() if this
        /// candidate is mapped, and the number of elements Revit will refuse to let you delete
        /// this type out from under if it is deleted without mapping first.
        /// </summary>
        public int UsageCount { get; set; }

        /// <summary>The mapping rule from corporate_standards.json that matched this candidate's Name, if any.</summary>
        public MappingRule ResolvedRule { get; set; }

        public MappingTargetSource TargetSource { get; set; } = MappingTargetSource.Unresolved;

        /// <summary>Id of the corporate-standard element (in the active document) this candidate maps to, once resolved.</summary>
        public ElementId ResolvedTargetElementId { get; set; } = ElementId.InvalidElementId;

        public string ResolvedTargetName { get; set; }

        /// <summary>Action selected by the user via the UI. PurgeBatchExecutor only acts on candidates where this is not None.</summary>
        public PurgeAction RequestedAction { get; set; } = PurgeAction.None;

        public PurgeCandidateStatus Status { get; set; } = PurgeCandidateStatus.Scanned;

        /// <summary>Human-readable detail for the current Status — populated on failure with the specific exception message.</summary>
        public string StatusDetail { get; set; }

        public PurgeCandidate(ElementId elementId, PurgeItemType itemType, string name)
        {
            if (elementId == null || elementId == ElementId.InvalidElementId)
                throw new ArgumentException("PurgeCandidate requires a valid ElementId.", nameof(elementId));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("PurgeCandidate requires a non-empty Name.", nameof(name));
            if (itemType == PurgeItemType.DwgImport)
                throw new ArgumentException(
                    "PurgeCandidate does not support DwgImport — use DwgImportReportEntry for DWG imports/links.",
                    nameof(itemType));

            ElementId = elementId;
            ItemType = itemType;
            Name = name;
        }
    }
}