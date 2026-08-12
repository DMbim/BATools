namespace BA.Core.Export.Models
{
    /// <summary>
    /// Plain summary of one type available for booklet generation, for
    /// populating the type picker without exposing ElementType to WPF.
    /// </summary>
    public class BookletTypeInfo
    {
        public string UniqueId { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;

        /// <summary>
        /// False when no placed instance of this type exists anywhere in
        /// the model. A real elevation/section needs an actual instance
        /// to cut through, a type with zero placed instances cannot get a
        /// booklet generated for it, this is not a permission issue, it's
        /// a fact about the model.
        /// </summary>
        public bool HasPlacedInstance { get; set; }
    }

    public class BookletOutcome
    {
        public string TypeName { get; set; } = string.Empty;

        public bool Skipped { get; set; }
        public string SkippedReason { get; set; } = string.Empty;

        public bool Success { get; set; }
        public string SheetNumber { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
