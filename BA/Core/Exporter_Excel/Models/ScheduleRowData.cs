using System.Collections.Generic;

namespace BA_Tools.ScheduleExporter.Models
{
    /// <summary>
    /// One element's exported data, keyed by ScheduleFieldMeta.ColumnIndex.
    /// Values are already converted to Excel-safe types (string, int, or double).
    /// </summary>
    public class ScheduleRowData
    {
        /// <summary>Revit ElementId.Value (long) — primary row key for reimport matching.</summary>
        public long ElementId { get; set; }

        /// <summary>Revit UniqueId string — fallback row key if ElementId lookup fails after file save.</summary>
        public string UniqueId { get; set; }

        /// <summary>
        /// Column values keyed by ScheduleFieldMeta.ColumnIndex.
        /// Value types: string for String/Double parameters, int for Integer, double for system IDs.
        /// </summary>
        public Dictionary<int, object> Values { get; set; } = new Dictionary<int, object>();
    }

    /// <summary>
    /// Per-cell state after parsing and comparing an imported Excel value.
    /// </summary>
    public class ImportCellData
    {
        /// <summary>Raw string value read from the Excel cell.</summary>
        public string RawValue { get; set; }

        /// <summary>State determined during compare phase.</summary>
        public ChangeState State { get; set; }

        /// <summary>Validation error message if State == Invalid.</summary>
        public string ValidationError { get; set; }
    }

    /// <summary>
    /// One row of data parsed from the Excel import file.
    /// Cells keyed by ScheduleFieldMeta.ColumnIndex.
    /// </summary>
    public class ImportRowData
    {
        /// <summary>ElementId.Value parsed from hidden column A.</summary>
        public long ElementId { get; set; }

        /// <summary>UniqueId parsed from hidden column B.</summary>
        public string UniqueId { get; set; }

        /// <summary>Per-cell import data keyed by ScheduleFieldMeta.ColumnIndex.</summary>
        public Dictionary<int, ImportCellData> Cells { get; set; } = new Dictionary<int, ImportCellData>();
    }
}
