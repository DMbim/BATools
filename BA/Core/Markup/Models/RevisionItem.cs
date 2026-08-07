// BA/Markup/Models/RevisionItem.cs
using System;

namespace BA.Markup.Models
{
    /// <summary>
    /// Full descriptor for a Revit Revision element.
    /// Populated by RevisionManagerHandler from the Revit API.
    /// All string fields are non-null. SequenceNumber is Revit-assigned and read-only.
    /// </summary>
    public sealed class RevisionItem
    {
        /// <summary>Revit ElementId integer value.</summary>
        public int ElementId { get; init; }

        /// <summary>
        /// Revit-assigned sequence number. Cannot be set via API.
        /// Read-only in both edit and create modes.
        /// On a newly created revision before the first refresh this will be 0.
        /// </summary>
        public int SequenceNumber { get; init; }

        /// <summary>Revision date string as stored in Revit (free-form text field).</summary>
        public string RevisionDate { get; init; } = string.Empty;

        /// <summary>Revision description.</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>Whether this revision has been marked as issued.</summary>
        public bool Issued { get; init; }

        /// <summary>Issued by field.</summary>
        public string IssuedBy { get; init; } = string.Empty;

        /// <summary>Issued to field.</summary>
        public string IssuedTo { get; init; } = string.Empty;

        /// <summary>
        /// Display name used in the MarkupWindow ComboBox.
        /// </summary>
        public string DisplayName =>
            $"{SequenceNumber} — {Description} ({RevisionDate})";

        public override string ToString() => DisplayName;

        /// <summary>
        /// Returns the string value of the named field for filtering and grouping.
        /// Field names match the property names exactly (case-insensitive).
        /// Returns empty string for unknown field names.
        /// </summary>
        public string GetFieldValue(string fieldName)
        {
            return fieldName?.ToLowerInvariant() switch
            {
                "sequencenumber" => SequenceNumber.ToString(),
                "revisiondate" => RevisionDate,
                "description" => Description,
                "issued" => Issued ? "Issued" : "Not Issued",
                "issuedby" => IssuedBy,
                "issuedto" => IssuedTo,
                _ => string.Empty
            };
        }

        /// <summary>
        /// All field names available for filtering and grouping, in display order.
        /// </summary>
        public static readonly string[] FilterableFields =
        {
            "SequenceNumber",
            "RevisionDate",
            "Description",
            "Issued",
            "IssuedBy",
            "IssuedTo"
        };
    }
}