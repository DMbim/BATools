using Autodesk.Revit.DB;

namespace BA_Tools.ScheduleExporter.Models
{
    /// <summary>
    /// Descriptor for a single visible column in a Revit schedule.
    /// One instance per exported column, stable across export and import sessions.
    /// </summary>
    public class ScheduleFieldMeta
    {
        /// <summary>
        /// Zero-based index of this column in the exported data (excludes hidden system columns A/B).
        /// Used as the key in ScheduleRowData.Values and ImportRowData.Cells.
        /// </summary>
        public int ColumnIndex { get; set; }

        /// <summary>Revit ScheduleFieldId — uniquely identifies the field within the schedule definition.</summary>
        public ScheduleFieldId FieldId { get; set; }

        /// <summary>
        /// ElementId of the backing parameter.
        /// For BuiltInParameters: ElementId whose Value equals the negative BuiltInParameter enum integer.
        /// For shared parameters: ElementId of the SharedParameterElement in the document.
        /// </summary>
        public ElementId ParameterId { get; set; }

        /// <summary>Column heading as shown in the Revit schedule (may differ from parameter name).</summary>
        public string DisplayName { get; set; }

        /// <summary>How this column is categorized for export/import treatment.</summary>
        public FieldCategory Category { get; set; }

        /// <summary>
        /// True when this column must never be written on import.
        /// Calculated, ElementIdType, and Hidden categories are always read-only.
        /// </summary>
        public bool IsReadOnly => Category == FieldCategory.Calculated
                               || Category == FieldCategory.ElementIdType
                               || Category == FieldCategory.Hidden;

        /// <summary>Revit StorageType of the backing parameter. None for Calculated/Hidden.</summary>
        public StorageType StorageType { get; set; }
    }
}
