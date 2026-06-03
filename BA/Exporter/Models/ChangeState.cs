namespace BA_Tools.ScheduleExporter.Models
{
    /// <summary>
    /// Per-cell state determined during import compare.
    /// </summary>
    public enum ChangeState
    {
        /// <summary>Value in Excel matches current Revit value — no write needed.</summary>
        Unchanged,

        /// <summary>Value in Excel differs from current Revit value — write will be attempted.</summary>
        Changed,

        /// <summary>Value in Excel could not be parsed or validated — write will be skipped with an error.</summary>
        Invalid,

        /// <summary>Column is read-only (Calculated, ElementIdType, Hidden) — write skipped by design.</summary>
        Skipped,

        /// <summary>The element this row belongs to no longer exists in the document.</summary>
        ElementDeleted
    }
}
