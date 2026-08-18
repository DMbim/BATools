// BA/Core/Parameters/ParamGroupCatalog.cs
// Extracted from the old RevitApiCompat.cs. InsertOrReinsert (the unsafe blind-replace binder)
// is deleted, it is fully replaced by SharedParameterBindingService's safe merge logic. These
// two classes are unrelated to binding, they are a picker catalog for parameter group dropdowns,
// so they get their own file rather than dying along with InsertOrReinsert.
using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace BA.Core.Parameters
{
    public sealed class ParamGroupPick
    {
        public ForgeTypeId GroupId { get; }
        public string Label { get; }
        public string Token { get; }

        public ParamGroupPick(ForgeTypeId id, string label, string token)
        {
            GroupId = id ?? GroupTypeId.Data;
            Label = label ?? "";
            Token = token ?? "Data";
        }

        public override string ToString() => Label;
    }

    public static class GroupCatalog
    {
        public static readonly ForgeTypeId DefaultGroupId = GroupTypeId.Data;

        public static readonly List<ParamGroupPick> CommonGroups = new()
        {
            new ParamGroupPick(GroupTypeId.Data, "Data", "Data"),
            new ParamGroupPick(GroupTypeId.IdentityData, "Identity Data", "IdentityData"),
            new ParamGroupPick(GroupTypeId.Text, "Text", "Text"),
            new ParamGroupPick(GroupTypeId.Constraints, "Constraints", "Constraints"),
            new ParamGroupPick(GroupTypeId.Materials, "Materials", "Materials"),
            new ParamGroupPick(GroupTypeId.Graphics, "Graphics", "Graphics"),
            new ParamGroupPick(GroupTypeId.Geometry, "Geometry", "Geometry"),
            new ParamGroupPick(GroupTypeId.AnalyticalProperties, "Analytical", "AnalyticalProperties"),
            new ParamGroupPick(GroupTypeId.Structural, "Structural", "Structural"),
            new ParamGroupPick(GroupTypeId.Visibility, "Visibility", "Visibility"),
        };
    }
}