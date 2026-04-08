namespace BA.Core.Standards
{
    public sealed class SubcategoryAuditOptions
    {
        /// <summary>
        /// When true, category-specific tolerated non-BA names are no longer tolerated.
        /// Revit built-in/global safe names remain allowed.
        /// </summary>
        public bool StrictMode { get; set; }

        /// <summary>
        /// When true, a family with no BA_* subcategories at all is reported as warning.
        /// </summary>
        public bool WarnIfNoBaNames { get; set; } = true;
    }
};