using Autodesk.Revit.DB;

namespace BA.UI.Parameters
{
    public sealed class CategoryPick
    {
        public CategoryPick(Category category)
        {
            Category = category;
            Name = category?.Name ?? string.Empty;
            CategoryIdValue = category?.Id?.Value ?? -1;

            CategoryType = category?.CategoryType.ToString() ?? "";
            AllowsBoundParameters = category?.AllowsBoundParameters ?? false;
            IsTagCategory = category?.IsTagCategory ?? false;
        }

        public Category Category { get; }
        public string Name { get; }
        public long CategoryIdValue { get; }

        // Debug/UX
        public string CategoryType { get; }
        public bool AllowsBoundParameters { get; }
        public bool IsTagCategory { get; }

        public bool IsSelected { get; set; }
    }
}
