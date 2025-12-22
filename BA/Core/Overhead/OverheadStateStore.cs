using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using View = Autodesk.Revit.DB.View;

namespace BA.Core.Overhead
{
    public static class OverheadStateStore
    {
        private static readonly Guid SchemaGuid = new("3F4A0B87-8F3E-42E1-8F0F-1C3A3B0D8D31");

        public static void SaveLastRun(Document doc, ElementId viewId, IList<ElementId> ids)
        {
            var view = doc.GetElement(viewId) as View;
            if (view == null) return;

            var schema = GetOrCreate();
            var ent = new Entity(schema);

            var longs = (ids ?? new List<ElementId>()).Select(ElementIdValue.Of);
            ent.Set(schema.GetField("OverriddenIdsCsv"), EsCsvCodec.EncodeLongs(longs));

            view.SetEntity(ent);
        }

        public static IList<ElementId> GetLastRunIds(Document doc, ElementId viewId)
        {
            var view = doc.GetElement(viewId) as View;
            if (view == null) return new List<ElementId>();

            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return new List<ElementId>();

            var ent = view.GetEntity(schema);
            if (!ent.IsValid()) return new List<ElementId>();

            var csv = ent.Get<string>(schema.GetField("OverriddenIdsCsv")) ?? string.Empty;
            return EsCsvCodec.DecodeLongs(csv).Select(v => new ElementId(v)).ToList();
        }

        public static bool WasOverridden(Document doc, ElementId viewId, ElementId elemId)
        {
            long target = ElementIdValue.Of(elemId);
            var last = GetLastRunIds(doc, viewId);
            return last.Any(id => ElementIdValue.Of(id) == target);
        }

        public static void ClearForView(Document doc, ElementId viewId)
        {
            var view = doc.GetElement(viewId) as View;
            if (view == null) return;

            var schema = GetOrCreate();
            var ent = new Entity(schema);
            ent.Set(schema.GetField("OverriddenIdsCsv"), string.Empty);
            view.SetEntity(ent);
        }

        private static Schema GetOrCreate()
        {
            var s = Schema.Lookup(SchemaGuid);
            if (s != null) return s;

            var sb = new SchemaBuilder(SchemaGuid);
            sb.SetSchemaName("OverheadAutoDash_StateCsv");
            sb.AddSimpleField("OverriddenIdsCsv", typeof(string));
            return sb.Finish();
        }
    }
}
