using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace BA.Core.Parameters
{
    public static class RevitApiCompat
    {
        public static bool InsertOrReinsert(Document doc, Definition def, ElementBinding binding, ForgeTypeId groupId)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            groupId ??= GroupTypeId.Data;

            var map = doc.ParameterBindings;
            if (map == null) return false;

            return map.Insert(def, binding, groupId) || map.ReInsert(def, binding, groupId);
        }
    }

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
