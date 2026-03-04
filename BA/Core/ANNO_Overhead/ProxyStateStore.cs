using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;
using System.Collections.Generic;
using System.Linq;
using View = Autodesk.Revit.DB.View;

namespace BA.Core.Overhead
{
    public static class ProxyStateStore
    {
        private static readonly Guid SchemaGuid = new("B8C2E3E1-0B59-4C0B-9F0A-4BFAE2F8E7A2");

        private const string F_OWNER_CSV = "OwnerIdsCsv";
        private const string F_PROXY_CSV = "ProxyIdsCsv";
        private const string ProxyCommentPrefix = "OAD_OVERHEAD_PROXY_";

        public static void AddProxies(View view, ElementId ownerId, IEnumerable<ElementId> proxyIds)
        {
            if (view == null) return;

            var (owners, proxies) = Read(view);
            long owner = ElementIdValue.Of(ownerId);

            foreach (var pid in proxyIds ?? Array.Empty<ElementId>())
            {
                owners.Add(owner);
                proxies.Add(ElementIdValue.Of(pid));
            }

            Write(view, owners, proxies);
        }

        public static void RemoveProxies(View view, ElementId ownerId)
        {
            if (view == null) return;

            var (owners, proxies) = Read(view);
            if (owners.Count == 0) return;

            var doc = view.Document;
            long owner = ElementIdValue.Of(ownerId);
            var toDelete = new List<ElementId>();

            for (int i = owners.Count - 1; i >= 0; i--)
            {
                if (owners[i] == owner)
                {
                    toDelete.Add(ToElementIdSafe(proxies[i]));
                    owners.RemoveAt(i);
                    proxies.RemoveAt(i);
                }
            }

            DeleteBestEffort(doc, toDelete);
            Write(view, owners, proxies);
        }

        public static int RemoveAllOverheadProxiesInViewBrute(Document doc, View view)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view == null) throw new ArgumentNullException(nameof(view));

            var toDelete = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(DetailCurve))
                .Cast<DetailCurve>()
                .Where(dc =>
                {
                    var p = dc.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    var s = p?.AsString() ?? "";
                    return s.StartsWith(ProxyCommentPrefix, StringComparison.OrdinalIgnoreCase);
                })
                .Select(dc => dc.Id)
                .ToList();

            if (toDelete.Count > 0)
                doc.Delete(toDelete);

            // Clear ES mapping (best effort)
            try { Write(view, new List<long>(), new List<long>()); } catch { }

            return toDelete.Count;
        }

        public static int RemoveAllOverheadProxiesAllPlansBrute(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            int deleted = 0;

            var plans = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(vp => vp.ViewType == ViewType.FloorPlan && !vp.IsTemplate)
                .ToList();

            foreach (var vp in plans)
            {
                try { deleted += RemoveAllOverheadProxiesInViewBrute(doc, vp); }
                catch { }
            }

            return deleted;
        }

        // ---------------- ES internals ----------------

        private static Schema GetOrCreate()
        {
            var s = Schema.Lookup(SchemaGuid);
            if (s != null) return s;

            var sb = new SchemaBuilder(SchemaGuid);
            sb.SetSchemaName("OverheadAutoDash_ProxyIndexCsv");
            sb.AddSimpleField(F_OWNER_CSV, typeof(string));
            sb.AddSimpleField(F_PROXY_CSV, typeof(string));
            return sb.Finish();
        }

        private static (List<long> owners, List<long> proxies) Read(View view)
        {
            var schema = GetOrCreate();
            var ent = view.GetEntity(schema);
            if (!ent.IsValid()) return (new List<long>(), new List<long>());

            var owners = EsCsvCodec.DecodeLongs(ent.Get<string>(schema.GetField(F_OWNER_CSV)) ?? "");
            var proxies = EsCsvCodec.DecodeLongs(ent.Get<string>(schema.GetField(F_PROXY_CSV)) ?? "");

            if (owners.Count != proxies.Count) { owners.Clear(); proxies.Clear(); }
            return (owners, proxies);
        }

        private static void Write(View view, List<long> owners, List<long> proxies)
        {
            var schema = GetOrCreate();
            var ent = new Entity(schema);
            ent.Set(schema.GetField(F_OWNER_CSV), EsCsvCodec.EncodeLongs(owners));
            ent.Set(schema.GetField(F_PROXY_CSV), EsCsvCodec.EncodeLongs(proxies));
            view.SetEntity(ent);
        }

        private static void DeleteBestEffort(Document doc, IList<ElementId> ids)
        {
            if (doc == null || ids == null || ids.Count == 0) return;
            try { doc.Delete(ids); } catch { }
        }

        private static ElementId ToElementIdSafe(long v)
        {
            if (v <= 0 || v > int.MaxValue) return ElementId.InvalidElementId;
            return new ElementId((int)v);
        }
    }
}