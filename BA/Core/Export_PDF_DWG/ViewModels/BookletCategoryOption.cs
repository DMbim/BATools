using Autodesk.Revit.DB;

namespace BA.ViewModels.Export
{
    /// <summary>
    /// A curated subset of categories relevant to booklet generation
    /// (windows, doors, casework/millwork, and similar), not every
    /// BuiltInCategory, that list is enormous and mostly irrelevant here.
    /// ParameterValue grouping mode covers anything outside this list.
    /// </summary>
    public class BookletCategoryOption
    {
        public string DisplayName { get; }
        public BuiltInCategory Category { get; }

        public BookletCategoryOption(string displayName, BuiltInCategory category)
        {
            DisplayName = displayName;
            Category = category;
        }

        public override string ToString() => DisplayName;
    }
}
