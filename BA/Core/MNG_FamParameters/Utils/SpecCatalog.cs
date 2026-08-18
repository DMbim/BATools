// BA/Core/SpecCatalog.cs
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BA.Core
{
    public sealed class SpecCatalogEntry
    {
        public string Label { get; }
        public ForgeTypeId SpecTypeId { get; }

        public SpecCatalogEntry(string label, ForgeTypeId specTypeId)
        {
            Label = !string.IsNullOrWhiteSpace(label) ? label : specTypeId?.TypeId ?? "<unknown>";
            SpecTypeId = specTypeId ?? throw new ArgumentNullException(nameof(specTypeId));
        }

        public override string ToString() => Label;
    }

    public static class SpecCatalog
    {
        private static IReadOnlyList<SpecCatalogEntry> _cache;

        /// <summary>
        /// Returns all SpecTypeId static ForgeTypeId properties from the Revit API via reflection,
        /// labelled via LabelUtils. Walks nested classes (SpecTypeId.Boolean, SpecTypeId.String, etc.).
        /// Result is cached after first call.
        /// </summary>
        public static IReadOnlyList<SpecCatalogEntry> GetAvailable()
        {
            if (_cache != null) return _cache;

            var entries = new List<SpecCatalogEntry>();

            void ScanType(Type t)
            {
                foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Static))
                {
                    if (prop.PropertyType != typeof(ForgeTypeId)) continue;
                    try
                    {
                        var id = (ForgeTypeId)prop.GetValue(null);
                        if (id == null) continue;
                        if (entries.Any(e => e.SpecTypeId.Equals(id))) continue;

                        string label;
                        try { label = LabelUtils.GetLabelForSpec(id); }
                        catch { label = prop.Name; }

                        entries.Add(new SpecCatalogEntry(label, id));
                    }
                    catch { }
                }

                foreach (var nested in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                    ScanType(nested);
            }

            ScanType(typeof(SpecTypeId));

            _cache = entries
                .OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return _cache;
        }

        public static SpecCatalogEntry FromForgeTypeId(ForgeTypeId id, IReadOnlyList<SpecCatalogEntry> options)
        {
            if (id == null || options == null) return null;
            return options.FirstOrDefault(e => e.SpecTypeId.Equals(id));
        }
    }
}