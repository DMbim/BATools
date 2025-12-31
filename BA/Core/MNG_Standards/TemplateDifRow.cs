// File: BA.Core/Standards/TemplateDiffRow.cs
namespace BA.Core.Standards
{
    public sealed class TemplateDiffRow
    {
        public bool ApplyFix { get; set; }          // UI checkbox

        // "Category", "Filter", "Workset", "Parameter", "FilterOrder"
        public string Scope { get; set; } = "";

        public string Name { get; set; } = "";
        public string Property { get; set; } = "";
        public string StandardValue { get; set; } = "";
        public string CurrentValue { get; set; } = "";
        public bool IsMismatch { get; set; }
        public string Notes { get; set; } = "";

        // Targets for Fix (filled by Compare)
        public long? TargetId { get; set; }         // CategoryId / ParamId / Workset key
        public string? TargetKey { get; set; }      // Filter name / special key
    }
}
