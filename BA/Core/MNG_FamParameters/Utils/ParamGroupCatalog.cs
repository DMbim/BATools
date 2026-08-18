using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BA.Core
{
    public sealed class ParamGroupPick
    {
        public string Name { get; }
        public string GroupTypeId { get; }   // ForgeTypeId.TypeId string
        public ForgeTypeId GroupId { get; }  // actual id

        public ParamGroupPick(string name, ForgeTypeId id)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "<null>" : name;
            GroupId = id ?? Autodesk.Revit.DB.GroupTypeId.Data;
            GroupTypeId = GroupId.TypeId;
        }

        public override string ToString() => Name;
    }

    public static class ParamGroupCatalog
    {
        /// <summary>
        /// Returns whatever GroupTypeId static ForgeTypeId properties exist in YOUR Revit API.
        /// Avoids missing constants across versions.
        /// </summary>
        public static IReadOnlyList<ParamGroupPick> GetAvailable()
        {
            var list = new List<ParamGroupPick>();

            var props = typeof(GroupTypeId).GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.PropertyType == typeof(ForgeTypeId));

            foreach (var p in props)
            {
                try
                {
                    var id = (ForgeTypeId)p.GetValue(null);
                    if (id == null) continue;
                    list.Add(new ParamGroupPick(p.Name, id));
                }
                catch { }
            }

            // ensure Data exists
            if (!list.Any(x => x.GroupId.Equals(GroupTypeId.Data)))
                list.Add(new ParamGroupPick("Data", GroupTypeId.Data));

            // Data first, then alphabetical
            return list
                .OrderByDescending(x => x.GroupId.Equals(GroupTypeId.Data))
                .ThenBy(x => x.Name)
                .ToList();
        }

        public static ParamGroupPick FromForgeTypeId(ForgeTypeId id, IReadOnlyList<ParamGroupPick> options)
        {
            id ??= GroupTypeId.Data;

            var hit = options?.FirstOrDefault(x => x.GroupId.Equals(id));
            if (hit != null) return hit;

            // ad-hoc entry (ensures UI can display even unknown ids)
            return new ParamGroupPick(id.TypeId, id);
        }
    }
}