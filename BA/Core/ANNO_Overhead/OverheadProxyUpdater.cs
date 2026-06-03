using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BA.Core.Overhead
{
    public sealed class OverheadProxyUpdater : IUpdater
    {
        private readonly AddInId _addInId;

        private static readonly Guid UpdaterGuid = new("8a4c9f75-5f6b-4a6b-8a4e-7a7c1a9a1111");

        // _syncLock guards _registered and _uid against startup/shutdown race conditions
        // in multi-document sessions where Register/Unregister can be called concurrently
        // from UIControlledApplication event handlers on different threads.
        private static readonly object _syncLock = new();
        private static UpdaterId? _uid;
        private static bool _registered;

        // Renamed from Suppress to IsSuppressed — .NET property naming convention.
        // Callers in OverheadDisableService and OverheadGlobalService must be updated.
        public static bool IsSuppressed { get; set; }

        // Global toggle (plugin-level). Distinct from the per-document settings.Enabled.
        public static bool Enabled { get; set; } = true;

        public OverheadProxyUpdater(AddInId addInId) => _addInId = addInId;

        // ═════════════════════════════════════════════════════════════════════════════════
        // IUpdater interface
        // ═════════════════════════════════════════════════════════════════════════════════

        public string GetAdditionalInformation() => "Keeps Overhead proxies in sync.";
        public ChangePriority GetChangePriority() => ChangePriority.Annotations;
        public UpdaterId GetUpdaterId() => new UpdaterId(_addInId, UpdaterGuid);
        public string GetUpdaterName() => "BA Overhead Proxy Updater";

        public void Execute(UpdaterData data)
        {
            if (!Enabled) return;
            if (IsSuppressed) return;

            var doc = data.GetDocument();
            if (doc == null) return;

            // Load returns null when settings have never been saved to this model.
            // Fall back to Default so Normalize is always called on a valid instance.
            var settings = OverheadSettingsStore.Load(doc, out _) ?? OverheadSettings.Default();
            settings.Normalize();

            if (!settings.Enabled) return;

            // Delete first — remove proxies for deleted owners before syncing modified.
            foreach (var delId in data.GetDeletedElementIds())
                RemoveProxiesForElementAllViews(doc, delId);

            var modified = new HashSet<ElementId>(data.GetModifiedElementIds());
            foreach (var id in data.GetAddedElementIds())
                modified.Add(id);

            if (modified.Count == 0) return;

            // Shared helper — eliminates the duplicate FilteredElementCollector
            // that was previously inlined here and in RemoveProxiesForElementAllViews.
            var plans = ProxyStateStore.GetFloorPlans(doc);

            foreach (var eid in modified)
            {
                var e = doc.GetElement(eid);
                if (e == null) continue;
                if (e.Category == null || e.Category.CategoryType != CategoryType.Model) continue;

                var elemLevelId = GetAssociatedLevelId(e);

                foreach (var vp in plans)
                {
                    if (vp.GenLevel == null) continue;
                    if (!ElementIdValue.IsValid(elemLevelId)) continue;
                    if (vp.GenLevel.Id != elemLevelId) continue;

                    SyncProxiesForElementInView(doc, vp, e, settings);
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════════════════
        // Registration
        // ═════════════════════════════════════════════════════════════════════════════════

        public static void Register(UIControlledApplication app)
        {
            lock (_syncLock)
            {
                if (_registered) return;

                var updater = new OverheadProxyUpdater(app.ActiveAddInId);
                _uid = updater.GetUpdaterId();

                try { UpdaterRegistry.UnregisterUpdater(_uid); } catch { }
                UpdaterRegistry.RegisterUpdater(updater, true);
                _registered = true;
            }

            // Called outside the lock because RefreshTriggers acquires _syncLock itself.
            RefreshTriggers(OverheadSettings.Default());
        }

        public static void RefreshTriggers(OverheadSettings settings)
        {
            lock (_syncLock)
            {
                if (!_registered || _uid == null) return;

                try { UpdaterRegistry.RemoveAllTriggers(_uid); } catch { }

                var cats = settings.SelectedCategories?.ToList()
                           ?? new List<BuiltInCategory> { BuiltInCategory.OST_Walls };

                ElementFilter filter = cats.Count == 1
                    ? (ElementFilter)new ElementCategoryFilter(cats[0])
                    : new LogicalOrFilter(
                        cats.ConvertAll<ElementFilter>(c => new ElementCategoryFilter(c)));

                UpdaterRegistry.AddTrigger(_uid, filter, Element.GetChangeTypeAny());
                UpdaterRegistry.AddTrigger(_uid, filter, Element.GetChangeTypeElementDeletion());
            }
        }

        public static void Unregister(UIControlledApplication app)
        {
            if (app == null) return;

            lock (_syncLock)
            {
                var uid = new UpdaterId(app.ActiveAddInId, UpdaterGuid);

                try { UpdaterRegistry.RemoveAllTriggers(uid); } catch { }
                try { UpdaterRegistry.UnregisterUpdater(uid); } catch { }

                _registered = false;
                _uid = null;
            }
        }

        // ═════════════════════════════════════════════════════════════════════════════════
        // Private helpers
        // ═════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Synchronises proxy DetailCurves for a single element in a single plan view.
        /// Wrapped in try-catch so a single element failure does not abort the updater
        /// batch and does not roll back the parent sub-transaction managed by the DMU framework.
        /// </summary>
        private static void SyncProxiesForElementInView(
            Document doc, ViewPlan view, Element e, OverheadSettings settings)
        {
            try
            {
                if (e.IsHidden(view) ||
                    (e.Category != null && view.GetCategoryHidden(e.Category.Id)))
                {
                    ProxyManager.RemoveProxies(view, e.Id);
                    return;
                }

                var (cutZ, topZ) = ViewRangeResolver.ResolveCutTopZ(doc, view, settings);

                var bb = e.get_BoundingBox(null);
                if (bb == null) { ProxyManager.RemoveProxies(view, e.Id); return; }

                double eps = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);

                bool inBand = (bb.Min.Z >= cutZ + eps) && (bb.Min.Z <= topZ);
                bool aboveCut = bb.Max.Z > (cutZ + eps);
                bool aboveTop = bb.Min.Z > topZ;

                // Delegates to OverheadAnalyzer.IsCutRequiredCategory (now internal) to
                // eliminate the duplicate hardcoded category list that existed here before.
                // Keeps proxy detection logic identical to OverheadAnalyzer.Run() so proxies
                // and overrides stay in sync at runtime.
                bool cutRequired = OverheadAnalyzer.IsCutRequiredCategory(e.Category);

                var gsOverhead = LineStyleLookup.FindOverhead(doc);
                if (gsOverhead == null) { ProxyManager.RemoveProxies(view, e.Id); return; }

                if ((cutRequired && inBand) || (aboveCut && aboveTop))
                    ProxyManager.CreateOrUpdateRectangleProxy(doc, view, e, bb, gsOverhead, settings);
                else
                    ProxyManager.RemoveProxies(view, e.Id);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(
                    $"[OverheadProxyUpdater] SyncProxiesForElementInView failed — " +
                    $"element {e?.Id}, view {view?.Id}: {ex.Message}");
                // Swallowed intentionally. A single element failure must not abort the
                // full updater batch — the proxy will be absent until next modification.
            }
        }

        /// <summary>
        /// Removes all proxies for a deleted element across every floor plan view.
        /// Uses ProxyStateStore.GetFloorPlans to avoid duplicating the collector.
        /// </summary>
        private static void RemoveProxiesForElementAllViews(Document doc, ElementId ownerId)
        {
            foreach (var vp in ProxyStateStore.GetFloorPlans(doc))
            {
                try
                {
                    ProxyManager.RemoveProxies(vp, ownerId);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(
                        $"[OverheadProxyUpdater] RemoveProxiesForElementAllViews failed — " +
                        $"element {ownerId}, view {vp.Id}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Walks the element's parameters to find an associated level ID.
        /// LevelId covers most cases; the BIP list covers hosted families and MEP elements
        /// that do not expose LevelId directly.
        /// </summary>
        private static ElementId GetAssociatedLevelId(Element e)
        {
            if (ElementIdValue.IsValid(e.LevelId)) return e.LevelId;

            foreach (var bip in new[]
            {
                BuiltInParameter.FAMILY_LEVEL_PARAM,
                BuiltInParameter.LEVEL_PARAM,
                BuiltInParameter.WALL_BASE_CONSTRAINT,
                BuiltInParameter.RBS_START_LEVEL_PARAM,
                BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM
            })
            {
                var p = e.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var id = p.AsElementId();
                    if (ElementIdValue.IsValid(id)) return id;
                }
            }

            return ElementId.InvalidElementId;
        }
    }
}