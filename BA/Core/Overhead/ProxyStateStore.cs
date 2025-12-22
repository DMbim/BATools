using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using View = Autodesk.Revit.DB.View;

namespace BA.Core.Overhead
{
    public static class ProxyStateStore
    {
        private static readonly Guid SchemaGuid = new("B8C2E3E1-0B59-4C0B-9F0A-4BFAE2F8E7A2");

        public static IList<ElementId> GetProxyIdsFor(View view, ElementId ownerId)
        {
            if (view == null) return new List<ElementId>();

            var (owners, proxies) = Read(view);
            var list = new List<ElementId>();
            long target = ElementIdValue.Of(ownerId);

            for (int i = 0; i < owners.Count; i++)
                if (owners[i] == target)
                    list.Add(new ElementId(proxies[i]));

            return list;
        }

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
            var (owners, proxies) = Read(view);
            if (owners.Count == 0) return;

            var doc = view.Document;
            long owner = ElementIdValue.Of(ownerId);
            var toDelete = new List<ElementId>();

            for (int i = owners.Count - 1; i >= 0; i--)
            {
                if (owners[i] == owner)
                {
                    toDelete.Add(new ElementId(proxies[i]));
                    owners.RemoveAt(i);
                    proxies.RemoveAt(i);
                }
            }

            if (toDelete.Count > 0)
            {
                try { doc.Delete(toDelete); } catch { }
            }

            Write(view, owners, proxies);
        }

        public static void ReplaceProxies(View view, ElementId ownerId, IEnumerable<ElementId> newProxyIds)
        {
            RemoveProxies(view, ownerId);
            AddProxies(view, ownerId, newProxyIds);
        }

        public static void ClearAll(View view)
        {
            if (view == null) return;

            var (owners, proxies) = Read(view);
            var doc = view.Document;

            foreach (var pid in proxies)
            {
                var id = new ElementId(pid);
                if (doc.GetElement(id) != null)
                {
                    try { doc.Delete(id); } catch { }
                }
            }

            Write(view, new List<long>(), new List<long>());
        }

        private static Schema GetOrCreate()
        {
            var s = Schema.Lookup(SchemaGuid);
            if (s != null) return s;

            var sb = new SchemaBuilder(SchemaGuid);
            sb.SetSchemaName("OverheadAutoDash_ProxyIndexCsv");
            sb.AddSimpleField("OwnerIdsCsv", typeof(string));
            sb.AddSimpleField("ProxyIdsCsv", typeof(string));
            return sb.Finish();
        }

        private static (List<long> owners, List<long> proxies) Read(View view)
        {
            var schema = GetOrCreate();
            var ent = view.GetEntity(schema);
            if (!ent.IsValid()) return (new List<long>(), new List<long>());

            var owners = EsCsvCodec.DecodeLongs(ent.Get<string>(schema.GetField("OwnerIdsCsv")) ?? string.Empty);
            var proxies = EsCsvCodec.DecodeLongs(ent.Get<string>(schema.GetField("ProxyIdsCsv")) ?? string.Empty);

            if (owners.Count != proxies.Count) { owners.Clear(); proxies.Clear(); }
            return (owners, proxies);
        }

        private static void Write(View view, List<long> owners, List<long> proxies)
        {
            var schema = GetOrCreate();
            var ent = new Entity(schema);
            ent.Set(schema.GetField("OwnerIdsCsv"), EsCsvCodec.EncodeLongs(owners));
            ent.Set(schema.GetField("ProxyIdsCsv"), EsCsvCodec.EncodeLongs(proxies));
            view.SetEntity(ent);
        }
    }
}
