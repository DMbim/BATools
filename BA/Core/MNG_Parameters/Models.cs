using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace BA.Core.Parameters
{
    public sealed class ParameterRow
    {
        public string Name { get; set; } = "";
        public bool IsShared { get; set; }
        public string Guid { get; set; } = "";

        public string InstanceOrType { get; set; } = "Instance";
        public string SpecLabel { get; set; } = "";
        public string GroupLabel { get; set; } = "";

        public ForgeTypeId GroupId { get; set; } = GroupCatalog.DefaultGroupId;
        public string CategoriesCsv { get; set; } = "";

        // Revit 2026: ElementId.Value (long)
        public List<long> CategoryIdValues { get; set; } = new();
    }


    public sealed class SharedDefRow
    {
        public string Name { get; set; } = "";
        public string GroupName { get; set; } = "";
        public Guid Guid { get; set; }

        // For UI
        public string Display => $"{GroupName} : {Name}";
    }

    public sealed class GroupRow
    {
        public string Name { get; set; } = "";
        public ForgeTypeId GroupId { get; set; } = GroupCatalog.DefaultGroupId;
    }

    public sealed class CategoryPick
    {
        public string Name { get; set; } = "";
        public long CategoryIdValue { get; set; } = -1;
        public bool IsSelected { get; set; }

        public CategoryPick() { }

        public CategoryPick(Category c)
        {
            Name = c?.Name ?? "";
            CategoryIdValue = c?.Id?.Value ?? -1;
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Lightweight picker model for selecting loadable families.
    /// </summary>
    public sealed class FamilyPick
    {
        public string Name { get; }
        public long FamilyIdValue { get; }
        public bool IsSelected { get; set; }

        public FamilyPick(Autodesk.Revit.DB.Family family)
        {
            Name = family?.Name ?? string.Empty;
            FamilyIdValue = family?.Id?.Value ?? -1;
        }
    }
}