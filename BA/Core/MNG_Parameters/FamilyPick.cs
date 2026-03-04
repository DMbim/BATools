using Autodesk.Revit.DB;

namespace BA.UI.Parameters
{
    public sealed class FamilyPick
    {
        public FamilyPick(Family family)
        {
            Family = family;
            Name = family?.Name ?? "";
            FamilyIdValue = family?.Id?.Value ?? -1;

            IsInPlace = family?.IsInPlace ?? false;
            // Note: loadable family is basically "Family element exists"; system families won't be in this list anyway
        }

        public Family Family { get; }
        public string Name { get; }
        public long FamilyIdValue { get; }

        public bool IsInPlace { get; }

        public bool IsSelected { get; set; }

        public string Display => IsInPlace ? $"{Name} (In-Place)" : Name;
    }
}
