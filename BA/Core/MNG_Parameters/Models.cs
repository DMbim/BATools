using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace BA.Core.Parameters
{
    public sealed class ParameterRow
    {
        public string Name { get; set; }
        public bool IsShared { get; set; }
        public string Guid { get; set; }
        public string InstanceOrType { get; set; } // "Instance" / "Type"
        public string SpecLabel { get; set; }
        public string GroupLabel { get; set; }
        public string CategoriesCsv { get; set; }

        public Definition Definition { get; set; }
        public ForgeTypeId GroupId { get; set; }

        public List<ElementId> CategoryIds { get; set; } = new();
    }

    public sealed class SharedDefRow
    {
        public string Name { get; set; }
        public string GroupName { get; set; }
        public Guid Guid { get; set; }
    }

    public sealed class CategoryPick
    {
        public Category Category { get; }
        public string Name => Category?.Name ?? "";
        public bool IsSelected { get; set; }

        public CategoryPick(Category c) { Category = c; }
    }

    public sealed class GroupPick
    {
        public ForgeTypeId GroupId { get; }
        public string Label { get; }

        public GroupPick(ForgeTypeId id, string label)
        {
            GroupId = id;
            Label = label;
        }

        public override string ToString() => Label;
    }
}
