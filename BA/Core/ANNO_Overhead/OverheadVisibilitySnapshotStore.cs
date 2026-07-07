using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BA.Core.Overhead
{
    /// <summary>
    /// Persists the exact set of view templates and non templated views whose
    /// BA_Overhead category visibility was flipped off by
    /// OverheadVgService.TurnOffBAOverheadEverywhere, so that
    /// OverheadVgService.TurnOnBAOverheadEverywhere can restore exactly those and
    /// only those elements, without touching views/templates that already had the
    /// category hidden for unrelated reasons.
    ///
    /// Stored on Document.ProjectInformation, which is guaranteed to exist in every
    /// project document, following the same document scoped ExtensibleStorage
    /// pattern already used elsewhere in this add in.
    /// </summary>
    internal static class OverheadVisibilitySnapshotStore
    {
        // DO NOT CHANGE THIS GUID. Changing it makes an in flight snapshot
        // (disabled but not yet re enabled) unreadable, which would leave the
        // BA_Overhead category permanently hidden in the affected views with no
        // recorded way to restore it.
        private static readonly Guid SchemaGuid = new("A3D1F0B2-6C7E-4A2B-9E6D-2B7C4F1A9D33");

        private const string F_TEMPLATE_CSV = "TemplateIdsCsv";
        private const string F_VIEW_CSV = "ViewIdsCsv";

        private static Schema? _cachedSchema;

        public static bool Save(Document doc, IEnumerable<ElementId> templateIds, IEnumerable<ElementId> viewIds)
        {
            if (doc?.ProjectInformation == null) return false;

            try
            {
                var schema = GetOrCreateSchema();
                var ent = new Entity(schema);

                var tLongs = (templateIds ?? Enumerable.Empty<ElementId>()).Select(ElementIdValue.Of).ToList();
                var vLongs = (viewIds ?? Enumerable.Empty<ElementId>()).Select(ElementIdValue.Of).ToList();

                ent.Set(schema.GetField(F_TEMPLATE_CSV), EsCsvCodec.EncodeLongs(tLongs));
                ent.Set(schema.GetField(F_VIEW_CSV), EsCsvCodec.EncodeLongs(vLongs));

                doc.ProjectInformation.SetEntity(ent);
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[OverheadVisibilitySnapshotStore] Save failed: {ex.Message}");
                return false;
            }
        }

        public static (List<ElementId> templateIds, List<ElementId> viewIds) Load(Document doc)
        {
            if (doc?.ProjectInformation == null) return (new List<ElementId>(), new List<ElementId>());

            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return (new List<ElementId>(), new List<ElementId>());

                var ent = doc.ProjectInformation.GetEntity(schema);
                if (!ent.IsValid()) return (new List<ElementId>(), new List<ElementId>());

                var tCsv = ent.Get<string>(schema.GetField(F_TEMPLATE_CSV)) ?? string.Empty;
                var vCsv = ent.Get<string>(schema.GetField(F_VIEW_CSV)) ?? string.Empty;

                var templateIds = EsCsvCodec.DecodeLongs(tCsv).Select(v => new ElementId(v)).ToList();
                var viewIds = EsCsvCodec.DecodeLongs(vCsv).Select(v => new ElementId(v)).ToList();

                return (templateIds, viewIds);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[OverheadVisibilitySnapshotStore] Load failed: {ex.Message}");
                return (new List<ElementId>(), new List<ElementId>());
            }
        }

        public static bool Clear(Document doc)
        {
            return Save(doc, Array.Empty<ElementId>(), Array.Empty<ElementId>());
        }

        private static Schema GetOrCreateSchema()
        {
            if (_cachedSchema != null && _cachedSchema.IsValidObject)
                return _cachedSchema;

            _cachedSchema = Schema.Lookup(SchemaGuid) ?? BuildSchema();
            return _cachedSchema;
        }

        private static Schema BuildSchema()
        {
            var sb = new SchemaBuilder(SchemaGuid);
            sb.SetSchemaName("OverheadAutoDash_VisibilitySnapshot");
            sb.AddSimpleField(F_TEMPLATE_CSV, typeof(string));
            sb.AddSimpleField(F_VIEW_CSV, typeof(string));
            return sb.Finish();
        }
    }
}