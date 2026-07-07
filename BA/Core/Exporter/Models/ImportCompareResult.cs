using System.Collections.Generic;

namespace BA_Tools.ScheduleExporter.Models
{
    /// <summary>
    /// Describes a proposed change to a type parameter, including all affected instances.
    /// A single warning covers one (typeId, parameterId) combination regardless of how many
    /// schedule rows reference the same type.
    /// </summary>
    public class TypeParameterWarning
    {
        public string ParameterName { get; set; }
        public string CurrentValue { get; set; }
        public string NewValue { get; set; }
        public string ElementTypeName { get; set; }
        public long ElementTypeId { get; set; }

        /// <summary>How many instances of this type exist in the entire document.</summary>
        public int AffectedInstanceCount { get; set; }

        /// <summary>
        /// True if multiple schedule rows referencing the same type specified different
        /// new values for this parameter — the last-encountered value will be used (last-write-wins).
        /// </summary>
        public bool HasConflict { get; set; }

        /// <summary>All distinct new values found across rows for this type/param combination.</summary>
        public List<string> ConflictingValues { get; set; } = new List<string>();
    }

    /// <summary>
    /// Full result of comparing imported Excel data against the current document state.
    /// Produced by ImportCompareService; consumed by ImportPreviewViewModel and ParameterWriteService.
    /// </summary>
    public class ImportCompareResult
    {
        public int TotalRows { get; set; }
        public int ChangedRowCount { get; set; }
        public int UnchangedRowCount { get; set; }
        public int SkippedCount { get; set; }
        public int InvalidValueCount { get; set; }

        /// <summary>ElementId.Value values from the import file whose elements no longer exist.</summary>
        public List<long> DeletedElementIds { get; set; } = new List<long>();

        /// <summary>One entry per (typeId, parameterId) combination that has a proposed type param change.</summary>
        public List<TypeParameterWarning> TypeParameterWarnings { get; set; } = new List<TypeParameterWarning>();

        /// <summary>Import rows that have at least one Changed cell and belong to existing elements.</summary>
        public List<ImportRowData> ProcessableRows { get; set; } = new List<ImportRowData>();
    }

    /// <summary>
    /// Result of a ParameterWriteService.WriteAll call.
    /// SuccessCount + FailureCount + SkippedCount should equal the number of attempted writes.
    /// </summary>
    public class WriteResult
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int SkippedCount { get; set; }
        public List<WriteError> Errors { get; set; } = new List<WriteError>();
    }

    /// <summary>
    /// Describes a single failed parameter write attempt.
    /// </summary>
    public class WriteError
    {
        public long ElementId { get; set; }
        public string ParameterName { get; set; }
        public string AttemptedValue { get; set; }
        public string ErrorMessage { get; set; }
    }
}
