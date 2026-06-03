namespace BA_Tools.ScheduleExporter.Models
{
    /// <summary>
    /// Classifies how a schedule field column should be treated during export/import.
    /// </summary>
    public enum FieldCategory
    {
        /// <summary>Instance parameter — fully editable, no fill in Excel.</summary>
        Instance,

        /// <summary>Type parameter — editable (blue fill), but write affects all instances of the type.</summary>
        TypeParameter,

        /// <summary>Formula, percentage, count, or derived field — locked (gray fill), never written on import.</summary>
        Calculated,

        /// <summary>Parameter with StorageType.ElementId — locked (gray fill), cannot be written via text.</summary>
        ElementIdType,

        /// <summary>System hidden columns: __ElementId and __UniqueId — hidden, always locked.</summary>
        Hidden
    }
}
