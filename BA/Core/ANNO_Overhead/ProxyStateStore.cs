using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using View = Autodesk.Revit.DB.View;

namespace BA.Core.Overhead
{
    public static class ProxyStateStore
    {
        // ── Schema identity ─────────────────────────────────────────────────────────────
        // Identifies the per-view extensible storage bucket for the BATools Overhead proxy
        // index. Each ViewPlan element stores one entity containing two parallel CSV string
        // fields: owner ElementIds and proxy DetailCurve ElementIds (one flat row per proxy
        // detail line; Owners[i] maps 1:1 to Proxies[i]).
        //
        // DO NOT CHANGE THIS GUID — it is persisted in Revit documents. Changing it
        // makes all existing proxy mappings unreadable, causing ghost proxy accumulation
        // with no recovery path short of a brute-force comment-scan cleanup.
        private static readonly Guid SchemaGuid = new("B8C2E3E1-0B59-4C0B-9F0A-4BFAE2F8E7A2");

        // Field name constants must match the persisted schema exactly.
        // These strings are stored in the Revit document — do not rename.
        private const string F_OWNER_CSV = "OwnerIdsCsv";
        private const string F_PROXY_CSV = "ProxyIdsCsv";

        // Written to ALL_MODEL_INSTANCE_COMMENTS by ProxyManager on every created
        // DetailCurve proxy. Used as the sole identification key for brute-force
        // cleanup when ES state is corrupt or unavailable (e.g. after a failed disable).
        internal const string ProxyCommentPrefix = "OAD_OVERHEAD_PROXY_";

        // ── Schema cache ─────────────────────────────────────────────────────────────────
        // Schema.Lookup iterates all registered schemas on every call — O(n) across all
        // loaded add-ins. Cache after first resolution to avoid this overhead on every
        // proxy read/write during active DMU execution.
        private static Schema? _cachedSchema;

        // ═════════════════════════════════════════════════════════════════════════════════
        // Public API
        // ═════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Appends N owner→proxy row pairs to the view's ES entity (one flat row per proxy ID).
        /// Does not remove prior entries for the same owner — call
        /// <see cref="RemoveProxies"/> first if replacing an existing mapping.
        /// Must be called inside an open transaction.
        /// Returns false if the write failed; cause logged via Trace.
        /// </summary>
        public static bool AddProxies(View view, ElementId ownerId, IEnumerable<ElementId> proxyIds)
        {
            if (view == null) return false;

            try
            {
                var (owners, proxies) = Read(view);
                long owner = ElementIdValue.Of(ownerId);

                foreach (var pid in proxyIds ?? Array.Empty<ElementId>())
                {
                    owners.Add(owner);
                    proxies.Add(ElementIdValue.Of(pid));
                }

                Write(view, owners, proxies);
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(
                    $"[ProxyStateStore] AddProxies failed — " +
                    $"view {view.Id}, owner {ownerId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Removes all rows for <paramref name="ownerId"/> from the view's ES entity and
        /// deletes the associated proxy DetailCurve elements from the document.
        ///
        /// Write order: ES is updated before element deletion. A failed delete leaves an
        /// orphaned DetailCurve in the model (recoverable by brute-force) but does NOT leave
        /// a ghost ES reference that would trigger repeated failed deletes on every updater tick.
        ///
        /// Must be called inside an open transaction.
        /// Returns false if any element deletion failed; cause logged via Trace.
        /// </summary>
        public static bool RemoveProxies(View view, ElementId ownerId)
        {
            if (view == null) return false;

            try
            {
                var (owners, proxies) = Read(view);
                if (owners.Count == 0) return true;

                var doc = view.Document;
                long owner = ElementIdValue.Of(ownerId);
                var toDelete = new List<ElementId>();
                bool anyFound = false;

                for (int i = owners.Count - 1; i >= 0; i--)
                {
                    if (owners[i] != owner) continue;

                    anyFound = true;
                    var id = LongToElementId(proxies[i]);

                    if (!ElementIdValue.IsValid(id))
                    {
                        Trace.WriteLine(
                            $"[ProxyStateStore] RemoveProxies: stored proxy ID {proxies[i]} " +
                            $"for owner {ownerId} in view {view.Id} is non-positive; skipping delete.");
                    }
                    else
                    {
                        toDelete.Add(id);
                    }

                    owners.RemoveAt(i);
                    proxies.RemoveAt(i);
                }

                if (!anyFound) return true; // Owner not tracked — not an error.

                Write(view, owners, proxies); // Persist before deleting — see method doc.
                return DeleteElements(doc, toDelete);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(
                    $"[ProxyStateStore] RemoveProxies failed — " +
                    $"view {view.Id}, owner {ownerId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Brute-force scans all DetailCurves in the given view and deletes those tagged
        /// with <see cref="ProxyCommentPrefix"/>. Use when ES state is corrupt or after
        /// a full disable sequence.
        /// Must be called inside an open transaction.
        /// Returns the number of elements deleted.
        /// </summary>
        public static int RemoveAllOverheadProxiesInViewBrute(Document doc, View view)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view == null) throw new ArgumentNullException(nameof(view));

            var targets = FindProxiesByComment(doc, view);

            if (targets.Count > 0)
            {
                try
                {
                    doc.Delete(targets);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(
                        $"[ProxyStateStore] RemoveAllOverheadProxiesInViewBrute: " +
                        $"batch delete failed for view {view.Id}: {ex.Message}");
                }
            }

            try
            {
                Write(view, new List<long>(), new List<long>());
            }
            catch (Exception ex)
            {
                Trace.WriteLine(
                    $"[ProxyStateStore] RemoveAllOverheadProxiesInViewBrute: " +
                    $"ES clear failed for view {view.Id}: {ex.Message}");
            }

            return targets.Count;
        }

        /// <summary>
        /// Brute-force scans all DetailCurves in every floor plan view and deletes those
        /// tagged with <see cref="ProxyCommentPrefix"/>. Use during full overhead disable.
        /// Must be called inside an open transaction.
        /// Returns the total number of elements deleted across all views.
        /// </summary>
        public static int RemoveAllOverheadProxiesAllPlansBrute(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            int deleted = 0;

            foreach (var vp in GetFloorPlans(doc))
            {
                try
                {
                    deleted += RemoveAllOverheadProxiesInViewBrute(doc, vp);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(
                        $"[ProxyStateStore] RemoveAllOverheadProxiesAllPlansBrute: " +
                        $"failed for view {vp.Id}: {ex.Message}");
                }
            }

            return deleted;
        }

        // ═════════════════════════════════════════════════════════════════════════════════
        // Internal helpers — accessible to OverheadProxyUpdater / OverheadAnalyzer
        // ═════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns all non-template floor plan views in the document.
        /// Centralised here so OverheadProxyUpdater does not duplicate the collector.
        /// </summary>
        internal static List<ViewPlan> GetFloorPlans(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(vp => vp.ViewType == ViewType.FloorPlan && !vp.IsTemplate)
                .ToList();
        }

        // ═════════════════════════════════════════════════════════════════════════════════
        // Private helpers
        // ═════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Collects ElementIds of all DetailCurves in <paramref name="view"/> whose
        /// ALL_MODEL_INSTANCE_COMMENTS begins with <see cref="ProxyCommentPrefix"/>.
        /// Extracted to eliminate the duplicated filter expression across all brute-force
        /// methods.
        /// </summary>
        private static List<ElementId> FindProxiesByComment(Document doc, View view)
        {
            return new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(DetailCurve))
                .Cast<DetailCurve>()
                .Where(dc =>
                {
                    var p = dc.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    return p?.AsString()
                             ?.StartsWith(ProxyCommentPrefix, StringComparison.OrdinalIgnoreCase)
                           == true;
                })
                .Select(dc => dc.Id)
                .ToList();
        }

        private static (List<long> owners, List<long> proxies) Read(View view)
        {
            var schema = GetOrCreateSchema();
            var ent = view.GetEntity(schema);
            if (!ent.IsValid()) return (new List<long>(), new List<long>());

            var owners = EsCsvCodec.DecodeLongs(ent.Get<string>(schema.GetField(F_OWNER_CSV)) ?? "");
            var proxies = EsCsvCodec.DecodeLongs(ent.Get<string>(schema.GetField(F_PROXY_CSV)) ?? "");

            if (owners.Count != proxies.Count)
            {
                Trace.WriteLine(
                    $"[ProxyStateStore] Read: count mismatch in view {view.Id} " +
                    $"(owners {owners.Count}, proxies {proxies.Count}); resetting to empty.");
                owners.Clear();
                proxies.Clear();
            }

            return (owners, proxies);
        }

        private static void Write(View view, List<long> owners, List<long> proxies)
        {
            var schema = GetOrCreateSchema();
            var ent = new Entity(schema);
            ent.Set(schema.GetField(F_OWNER_CSV), EsCsvCodec.EncodeLongs(owners));
            ent.Set(schema.GetField(F_PROXY_CSV), EsCsvCodec.EncodeLongs(proxies));
            view.SetEntity(ent);
        }

        private static bool DeleteElements(Document doc, IList<ElementId> ids)
        {
            if (ids == null || ids.Count == 0) return true;

            try
            {
                doc.Delete(ids);
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(
                    $"[ProxyStateStore] DeleteElements: " +
                    $"batch delete of {ids.Count} element(s) failed: {ex.Message}");
                return false;
            }
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
            sb.SetSchemaName("OverheadAutoDash_ProxyIndexCsv");
            sb.AddSimpleField(F_OWNER_CSV, typeof(string));
            sb.AddSimpleField(F_PROXY_CSV, typeof(string));
            return sb.Finish();
        }

        /// <summary>
        /// Converts a raw long stored in ES to an ElementId.
        /// Returns <see cref="ElementId.InvalidElementId"/> for non-positive values.
        /// No int truncation applied — Revit 2024+ uses 64-bit element IDs.
        /// Formerly named ToElementIdSafe; renamed for clarity.
        /// </summary>
        private static ElementId LongToElementId(long v)
        {
            if (v <= 0) return ElementId.InvalidElementId;
            return new ElementId(v);
        }
    }
}