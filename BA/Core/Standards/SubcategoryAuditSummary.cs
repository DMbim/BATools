namespace BA.Core.Standards
{
    public sealed class SubcategoryAuditSummary
    {
        public int TotalRows { get; set; }
        public int CleanCount { get; set; }
        public int WarningCount { get; set; }
        public int ErrorCount { get; set; }
        public int SkippedCount { get; set; }

        public int MissingRequiredCount { get; set; }
        public int NonCompliantNameCount { get; set; }
        public int AllowedNonBaCount { get; set; }
        public int ValidBaCount { get; set; }
        public int NoBaNamesCount { get; set; }
    }
}