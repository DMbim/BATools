using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace BA.Core.Parameters
{
    /// <summary>
    /// Revit 2026-only compatibility helpers.
    /// No legacy fallbacks (no BuiltInParameterGroup, no reflection Insert overload hunting).
    /// </summary>
    public static class RevitApiCompat
    {
        /// <summary>
        /// Revit 2026: ElementId.Value exists and is the supported int accessor.
        /// </summary>
        public static long IdLong(ElementId id) => id?.Value ?? -1L;

        /// <summary>
        /// Revit 2026: GetLabelForSpec exists.
        /// </summary>
        public static string SafeSpecLabel(ForgeTypeId specId)
        {
            if (specId == null) return "";
            try { return LabelUtils.GetLabelForSpec(specId); }
            catch { return specId.TypeId ?? specId.ToString(); }
        }

        /// <summary>
        /// Revit 2026: GetLabelForGroup(ForgeTypeId) exists.
        /// </summary>
        public static string SafeGroupLabel(ForgeTypeId groupId)
        {
            if (groupId == null) return "";
            try { return LabelUtils.GetLabelForGroup(groupId); }
            catch { return groupId.TypeId ?? groupId.ToString(); }
        }

        /// <summary>
        /// Revit 2026: BindingMap supports Insert/ReInsert with ForgeTypeId group.
        /// We try Insert first; if it returns false, we ReInsert.
        /// </summary>
        public static bool InsertOrReinsert(Document doc, Definition def, ElementBinding binding, ForgeTypeId groupId)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            if (groupId == null) groupId = GroupTypeId.Data;

            var map = doc.ParameterBindings;
            if (map == null) return false;

            // Insert returns false if already present, so fallback to ReInsert
            return map.Insert(def, binding, groupId) || map.ReInsert(def, binding, groupId);
        }

        /// <summary>
        /// Resolve parameter group from a simple token. Revit 2026-only set.
        /// Accepts tokens like: "Data", "IdentityData", "Text", "Constraints", ...
        /// Also accepts legacy-like "PG_DATA" by mapping to Data (without using the legacy enum).
        /// </summary>
        public static ForgeTypeId ResolveGroupTypeId(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return GroupTypeId.Data;

            token = token.Trim();

            // tolerate PG_ prefix without referencing BuiltInParameterGroup
            if (token.StartsWith("PG_", StringComparison.OrdinalIgnoreCase))
                token = token.Substring(3);

            switch (token.ToLowerInvariant())
            {
                case "data": return GroupTypeId.Data;
                case "identitydata":
                case "identity": return GroupTypeId.IdentityData;
                case "text": return GroupTypeId.Text;
                case "constraints": return GroupTypeId.Constraints;
                case "materials":
                case "material": return GroupTypeId.Materials;
                case "graphics": return GroupTypeId.Graphics;
                case "geometry": return GroupTypeId.Geometry;
                case "analyticalproperties":
                case "analytical": return GroupTypeId.AnalyticalProperties;
                case "structural": return GroupTypeId.Structural;
                case "visibility": return GroupTypeId.Visibility;

                // If you REALLY need "Dimensions" and your Revit API exposes it,
                // add it here explicitly:
                // case "dimensions": return GroupTypeId.Dimensions;

                default:
                    return GroupTypeId.Data;
            }
        }
    }

    /// <summary>
    /// Renamed to avoid collision with your existing GroupPick.
    /// Use this in ComboBoxes.
    /// </summary>
    public sealed class ParamGroupPick
    {
        public ForgeTypeId Id { get; }
        public string Label { get; }
        public string Token { get; }

        // ✅ Back-compat alias for existing UI code
        public ForgeTypeId GroupId => Id;

        public ParamGroupPick(ForgeTypeId id, string label, string token)
        {
            Id = id ?? GroupTypeId.Data;
            Label = label ?? "";
            Token = token ?? "Data";
        }

        public override string ToString() => Label;
    }

    /// <summary>
    /// Groups for UI pickers. Revit 2026-only list (no members that might not exist).
    /// </summary>
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
