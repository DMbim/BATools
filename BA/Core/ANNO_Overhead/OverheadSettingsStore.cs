using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BA.Core.Overhead
{
    public static class OverheadSettingsStore
    {
        // legacy
        private static readonly Guid SchemaGuidV1 = new("C9F2B0C1-1F9A-4B8D-8F25-9A7E2F0B3C10");
        private static readonly Guid SchemaGuidV2 = new("8A1F8D2E-4B5C-4C2F-B9C3-2D3E8B7A90F1");
        private static readonly Guid SchemaGuidV3 = new("2F4B7E92-1E6B-4F6A-8D4A-1F7E8C2B9C31");

        // new
        private static readonly Guid SchemaGuidV4 = new("3D1D7B5F-9A34-4D2E-9C43-5B1D81B4D2A6");

        private const string F_ENABLED = "Enabled";
        private const string F_USE_NEXT_LEVEL = "UseNextLevelAsTop";
        private const string F_FALLBACK_STR = "FallbackCutMmStr";
        private const string F_TINY_STR = "TinyThresholdMmStr";
        private const string F_MINEDGE_STR = "MinProxyEdgeMmStr";
        private const string F_CATS_CSV = "SelectedCategoriesCsv";

        public static OverheadSettings? Load(Document doc, out bool migrate)
        {
            migrate = false;
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            // V4
            var s4 = Schema.Lookup(SchemaGuidV4);
            if (s4 != null)
            {
                var e4 = doc.ProjectInformation.GetEntity(s4);
                if (e4.IsValid())
                    return ReadV4(e4, s4);
            }

            // V3 -> migrate to V4
            var s3 = Schema.Lookup(SchemaGuidV3);
            if (s3 != null)
            {
                var e3 = doc.ProjectInformation.GetEntity(s3);
                if (e3.IsValid())
                {
                    var s = ReadV3(e3, s3);
                    s.Enabled = true;
                    Save(doc, s);
                    return s;
                }
            }

            // V2 -> migrate
            var s2 = Schema.Lookup(SchemaGuidV2);
            if (s2 != null)
            {
                var e2 = doc.ProjectInformation.GetEntity(s2);
                if (e2.IsValid())
                {
                    var s = ReadV2(e2, s2);
                    s.Enabled = true;
                    Save(doc, s);
                    return s;
                }
            }

            // V1 -> migrate
            var s1 = Schema.Lookup(SchemaGuidV1);
            if (s1 != null)
            {
                var e1 = doc.ProjectInformation.GetEntity(s1);
                if (e1.IsValid())
                {
                    var s = ReadV1(e1, s1);
                    s.Enabled = true;
                    Save(doc, s);
                    return s;
                }
            }

            return null;
        }

        public static void Save(Document doc, OverheadSettings s)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            s ??= OverheadSettings.Default();
            s.Normalize();

            var schema = GetOrCreateV4();
            var ent = new Entity(schema);

            ent.Set(schema.GetField(F_ENABLED), s.Enabled);
            ent.Set(schema.GetField(F_USE_NEXT_LEVEL), s.UseNextLevelAsTop);
            ent.Set(schema.GetField(F_FALLBACK_STR), s.FallbackCutMm.ToString(CultureInfo.InvariantCulture));
            ent.Set(schema.GetField(F_TINY_STR), s.TinyThresholdMm.ToString(CultureInfo.InvariantCulture));
            ent.Set(schema.GetField(F_MINEDGE_STR), s.MinProxyEdgeMm.ToString(CultureInfo.InvariantCulture));

            var csv = EsCsvCodec.EncodeInts((s.SelectedCategories ?? new HashSet<BuiltInCategory>()).Select(x => (int)x));
            ent.Set(schema.GetField(F_CATS_CSV), csv);

            doc.ProjectInformation.SetEntity(ent);
        }

        private static Schema GetOrCreateV4()
        {
            var s = Schema.Lookup(SchemaGuidV4);
            if (s != null) return s;

            var sb = new SchemaBuilder(SchemaGuidV4);
            sb.SetSchemaName("OverheadAutoDash_SettingsCsv_v4");

            sb.AddSimpleField(F_ENABLED, typeof(bool));
            sb.AddSimpleField(F_USE_NEXT_LEVEL, typeof(bool));
            sb.AddSimpleField(F_FALLBACK_STR, typeof(string));
            sb.AddSimpleField(F_TINY_STR, typeof(string));
            sb.AddSimpleField(F_MINEDGE_STR, typeof(string));
            sb.AddSimpleField(F_CATS_CSV, typeof(string));

            return sb.Finish();
        }

        private static OverheadSettings ReadV4(Entity e, Schema s)
        {
            var set = new OverheadSettings
            {
                Enabled = SafeGetBool(e, s, F_ENABLED, true),
                UseNextLevelAsTop = SafeGetBool(e, s, F_USE_NEXT_LEVEL, true),
                FallbackCutMm = ParseDouble(SafeGetString(e, s, F_FALLBACK_STR), 1200.0),
                TinyThresholdMm = ParseDouble(SafeGetString(e, s, F_TINY_STR), 50.0),
                MinProxyEdgeMm = ParseDouble(SafeGetString(e, s, F_MINEDGE_STR), 0.5),
            };

            var csv = SafeGetString(e, s, F_CATS_CSV) ?? "";
            var ints = EsCsvCodec.DecodeInts(csv);
            set.SelectedCategories = new HashSet<BuiltInCategory>(ints.Select(i => (BuiltInCategory)i));

            set.Normalize();
            return set;
        }

        private static OverheadSettings ReadV3(Entity e, Schema s)
        {
            var set = new OverheadSettings
            {
                Enabled = true,
                UseNextLevelAsTop = e.Get<bool>(s.GetField(F_USE_NEXT_LEVEL)),
                FallbackCutMm = ParseDouble(e.Get<string>(s.GetField(F_FALLBACK_STR)), 1200.0),
                TinyThresholdMm = ParseDouble(e.Get<string>(s.GetField(F_TINY_STR)), 50.0),
                MinProxyEdgeMm = 0.5
            };

            var csv = e.Get<string>(s.GetField(F_CATS_CSV)) ?? "";
            var ints = EsCsvCodec.DecodeInts(csv);
            set.SelectedCategories = new HashSet<BuiltInCategory>(ints.Select(i => (BuiltInCategory)i));

            set.Normalize();
            return set;
        }

        private static OverheadSettings ReadV2(Entity e, Schema s)
        {
            var set = new OverheadSettings
            {
                Enabled = true,
                UseNextLevelAsTop = e.Get<bool>(s.GetField(F_USE_NEXT_LEVEL)),
                FallbackCutMm = e.Get<double>(s.GetField("FallbackCutMm")),
                TinyThresholdMm = e.Get<double>(s.GetField("TinyThresholdMm")),
                MinProxyEdgeMm = 0.5
            };

            var csv = e.Get<string>(s.GetField(F_CATS_CSV)) ?? "";
            var ints = EsCsvCodec.DecodeInts(csv);
            set.SelectedCategories = new HashSet<BuiltInCategory>(ints.Select(i => (BuiltInCategory)i));

            set.Normalize();
            return set;
        }

        private static OverheadSettings ReadV1(Entity e, Schema s)
        {
            // Same shape as V2 for these fields
            return ReadV2(e, s);
        }

        private static string? SafeGetString(Entity e, Schema s, string fieldName)
        {
            try { return e.Get<string>(s.GetField(fieldName)); } catch { return null; }
        }

        private static bool SafeGetBool(Entity e, Schema s, string fieldName, bool fallback)
        {
            try { return e.Get<bool>(s.GetField(fieldName)); } catch { return fallback; }
        }

        private static double ParseDouble(string? s, double fallback)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}