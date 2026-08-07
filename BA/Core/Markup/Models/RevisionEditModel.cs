// BA/Markup/Models/RevisionEditModel.cs
using System;

namespace BA.Markup.Models
{
    /// <summary>
    /// Mutable DTO used by RevisionEditorViewModel.
    /// Populated from a RevisionItem for edit, or blank for create.
    /// Passed to RevisionManagerHandler to write back to Revit.
    /// </summary>
    public sealed class RevisionEditModel
    {
        /// <summary>
        /// ElementId of the revision being edited.
        /// -1 signals a create operation (no existing element).
        /// </summary>
        public int ElementId { get; set; } = -1;

        /// <summary>Read-only — Revit assigns this. Displayed but never written.</summary>
        public int SequenceNumber { get; set; }

        public string RevisionDate { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Issued { get; set; }
        public string IssuedBy { get; set; } = string.Empty;
        public string IssuedTo { get; set; } = string.Empty;

        public bool IsNew => ElementId < 0;

        /// <summary>
        /// Constructs a blank model for creating a new revision.
        /// </summary>
        public static RevisionEditModel ForCreate() => new()
        {
            ElementId = -1,
            RevisionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Description = string.Empty,
            Issued = false,
            IssuedBy = string.Empty,
            IssuedTo = string.Empty
        };

        /// <summary>
        /// Constructs a populated model from an existing RevisionItem.
        /// </summary>
        public static RevisionEditModel FromRevisionItem(RevisionItem item) => new()
        {
            ElementId = item.ElementId,
            SequenceNumber = item.SequenceNumber,
            RevisionDate = item.RevisionDate,
            Description = item.Description,
            Issued = item.Issued,
            IssuedBy = item.IssuedBy,
            IssuedTo = item.IssuedTo
        };
    }
}