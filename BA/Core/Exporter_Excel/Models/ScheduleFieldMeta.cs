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

        public ScheduleFieldId FieldId     { get; set; }
        public ElementId       ParameterId { get; set; }

        /// <summary>Column heading as shown in the Revit schedule.</summary>
        public string DisplayName { get; set; }

        public FieldCategory Category    { get; set; }
        public StorageType   StorageType { get; set; }

        /// <summary>
        /// Revit spec type ForgeTypeId for Double parameters (e.g. SpecTypeId.Length).
        /// Used to display the unit label in the Excel data type row and to parse
        /// display-unit strings back to internal values on import.
        /// Null for non-Double parameters.
        /// </summary>
        public ForgeTypeId SpecTypeId { get; set; }

        /// <summary>
        /// Human-readable data type label for the Excel row 2, e.g.:
        ///   "Text",  "Integer",  "Length (m)",  "Area (m²)",
        ///   "TYPE · Text",  "Calculated (formula)",  "Reference"
        /// Populated by ScheduleReaderService after StorageType probe.
        /// </summary>
        public string DataTypeLabel { get; set; }

        /// <summary>
        /// True when this column must never be written on import.
        /// Calculated, ElementIdType and Hidden categories are always read-only.
        /// </summary>
        public bool IsReadOnly => Category == FieldCategory.Calculated
                               || Category == FieldCategory.ElementIdType
                               || Category == FieldCategory.Hidden;
    }
}
