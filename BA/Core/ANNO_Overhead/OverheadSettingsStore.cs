using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace BA.Core.Overhead
{
    public static class OverheadSettingsStore
    {
        // Keep GUIDs => keeps backward compatibility with existing stored settings.
        private static readonly Guid SchemaGuidV3 = new("2F4B7E92-1E6B-4F6A-8D4A-1F7E8C2B9C31");
        private static readonly Guid SchemaGuidV2 = new("8A1F8D2E-4B5C-4C2F-B9C3-2D3E8B7A90F1");
        private static readonly Guid SchemaGuidV1 = new("C9F2B0C1-1F9A-4B8D-8F25-9A7E2F0B3C10");

        public static OverheadSettings? Load(Document doc)
        {
            var sV3 = Schema.Lookup(SchemaGuidV3);
            if (sV3 != null)
            {
                var e3 = doc.ProjectInformation.GetEntity(sV3);
                if (e3.IsValid()) return ReadV3(e3, sV3);
            }

            var sV2 = Schema.Lookup(SchemaGuidV2);
            if (sV2 != null)
            {
                var e2 = doc.ProjectInformation.GetEntity(sV2);
                if (e2.IsValid())
                {
                    var s = ReadV2(e2, sV2);
                    Save(doc, s);
                    return s;
                }
            }

            var sV1 = Schema.Lookup(SchemaGuidV1);
            if (sV1 != null)
            {
                var e1 = doc.ProjectInformation.GetEntity(sV1);
                if (e1.IsValid())
                {
                    var s = ReadV1(e1, sV1);
                    Save(doc, s);
                    return s;
                }
            }

            return null;
        }

        public static void Save(Document doc, OverheadSettings s)
        {
            s ??= OverheadSettings.Default();
            s.Normalize();

            var schema = GetOrCreateV3();
            var ent = new Entity(schema);

            ent.Set(schema.GetField("UseNextLevelAsTop"), s.UseNextLevelAsTop);
            ent.Set(schema.GetField("FallbackCutMmStr"), s.FallbackCutMm.ToString(CultureInfo.InvariantCulture));
            ent.Set(schema.GetField("TinyThresholdMmStr"), s.TinyThresholdMm.ToString(CultureInfo.InvariantCulture));

            var catsCsv = EsCsvCodec.EncodeInts(s.SelectedCategories.Select(c => (int)c));
            ent.Set(schema.GetField("SelectedCategoriesCsv"), catsCsv);

            doc.ProjectInformation.SetEntity(ent);
        }

        private static Schema GetOrCreateV3()
        {
            var s = Schema.Lookup(SchemaGuidV3);
            if (s != null) return s;

            var sb = new SchemaBuilder(SchemaGuidV3);
            sb.SetSchemaName("OverheadAutoDash_SettingsCsv_v3"); // keep name (schema name cannot be changed later)
            sb.AddSimpleField("UseNextLevelAsTop", typeof(bool));
            sb.AddSimpleField("FallbackCutMmStr", typeof(string));
            sb.AddSimpleField("TinyThresholdMmStr", typeof(string));
            sb.AddSimpleField("SelectedCategoriesCsv", typeof(string));
            return sb.Finish();
        }

        private static OverheadSettings ReadV3(Entity e, Schema s)
        {
            var set = new OverheadSettings
            {
                UseNextLevelAsTop = e.Get<bool>(s.GetField("UseNextLevelAsTop")),
                FallbackCutMm = ParseDouble(e.Get<string>(s.GetField("FallbackCutMmStr")), 1200.0),
                TinyThresholdMm = ParseDouble(e.Get<string>(s.GetField("TinyThresholdMmStr")), 50.0)
            };

            var csv = e.Get<string>(s.GetField("SelectedCategoriesCsv")) ?? string.Empty;
            var ints = EsCsvCodec.DecodeInts(csv);
            set.SelectedCategories = new HashSet<BuiltInCategory>(ints.Select(i => (BuiltInCategory)i));
            set.Normalize();
            return set;
        }

        private static OverheadSettings ReadV2(Entity e, Schema s)
        {
            var set = new OverheadSettings
            {
                UseNextLevelAsTop = e.Get<bool>(s.GetField("UseNextLevelAsTop")),
                FallbackCutMm = e.Get<double>(s.GetField("FallbackCutMm")),
                TinyThresholdMm = e.Get<double>(s.GetField("TinyThresholdMm"))
            };
            var csv = e.Get<string>(s.GetField("SelectedCategoriesCsv")) ?? string.Empty;
            var ints = EsCsvCodec.DecodeInts(csv);
            set.SelectedCategories = new HashSet<BuiltInCategory>(ints.Select(i => (BuiltInCategory)i));
            set.Normalize();
            return set;
        }

        private static OverheadSettings ReadV1(Entity e, Schema s)
        {
            var set = new OverheadSettings
            {
                UseNextLevelAsTop = e.Get<bool>(s.GetField("UseNextLevelAsTop")),
                FallbackCutMm = e.Get<double>(s.GetField("FallbackCutMm")),
                TinyThresholdMm = e.Get<double>(s.GetField("TinyThresholdMm"))
            };
            var csv = e.Get<string>(s.GetField("SelectedCategoriesCsv")) ?? string.Empty;
            var ints = EsCsvCodec.DecodeInts(csv);
            set.SelectedCategories = new HashSet<BuiltInCategory>(ints.Select(i => (BuiltInCategory)i));
            set.Normalize();
            return set;
        }

        private static double ParseDouble(string? s, double fallback)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
