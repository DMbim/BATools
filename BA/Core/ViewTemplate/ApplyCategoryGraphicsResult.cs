using System.Collections.Generic;

namespace BA.Core.ViewTemplates
{
    public sealed class ApplyCategoryGraphicsResult
    {
        public string SourceTemplateName { get; set; } = string.Empty;
        public int RequestedTargets { get; set; }
        public int UpdatedTargets { get; set; }
        public int SkippedTargets { get; set; }
        public int RequestedCategories { get; set; }
        public int AppliedCategoriesPerTarget { get; set; }

        public List<string> Messages { get; } = new List<string>();
    }
}