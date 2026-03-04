using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Overhead
{
    public sealed class OverheadProxyUpdater : IUpdater
    {
        private readonly AddInId _addInId;

        private static readonly Guid UpdaterGuid = new("8a4c9f75-5f6b-4a6b-8a4e-7a7c1a9a1111");
        private static UpdaterId _uid;
        private static bool _registered;

        public static bool Suppress { get; set; }

        // ✅ GLOBAL TOGGLE (plugin-level)
        public static bool Enabled { get; set; } = true;

        public OverheadProxyUpdater(AddInId addInId) => _addInId = addInId;

        public void Execute(UpdaterData data)
        {
            if (!Enabled) return;
            if (Suppress) return;

            var doc = data.GetDocument();
            if (doc == null) return;

            var settings = OverheadSettingsStore.Load(doc, out bool migrate) ?? OverheadSettings.Default();
            settings.Normalize();

            // ✅ PER-DOC toggle stored in model
            if (!settings.Enabled)
                return;

            // delete first (remove proxies for deleted owners)
            foreach (var delId in data.GetDeletedElementIds())
                RemoveProxiesForElementAllViews(doc, delId);

            var modified = new HashSet<ElementId>(data.GetModifiedElementIds());
            foreach (var id in data.GetAddedElementIds())
                modified.Add(id);

            if (modified.Count == 0) return;

            var plans = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(vp => vp.ViewType == ViewType.FloorPlan && !vp.IsTemplate)
                .ToList();

            var modifiedIds = modified.ToList();

            foreach (var eid in modifiedIds)
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

        private static void SyncProxiesForElementInView(Document doc, ViewPlan view, Element e, OverheadSettings settings)
        {
            if (e.IsHidden(view) || (e.Category != null && view.GetCategoryHidden(e.Category.Id)))
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

            bool cutRequired =
                e.Category != null &&
                (e.Category.Id == new ElementId(BuiltInCategory.OST_Walls)
                 || e.Category.Id == new ElementId(BuiltInCategory.OST_StructuralColumns));

            var gsOverhead = LineStyleLookup.FindOverhead(doc);
            if (gsOverhead == null) { ProxyManager.RemoveProxies(view, e.Id); return; }

            if ((cutRequired && inBand) || (aboveCut && aboveTop))
                ProxyManager.CreateOrUpdateRectangleProxy(doc, view, e, bb, gsOverhead, settings);
            else
                ProxyManager.RemoveProxies(view, e.Id);
        }

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

        public string GetAdditionalInformation() => "Keeps Overhead proxies in sync.";
        public ChangePriority GetChangePriority() => ChangePriority.Annotations;
        public UpdaterId GetUpdaterId() => new UpdaterId(_addInId, UpdaterGuid);
        public string GetUpdaterName() => "BA Overhead Proxy Updater";

        public static void Register(UIControlledApplication app)
        {
            var updater = new OverheadProxyUpdater(app.ActiveAddInId);
            _uid = updater.GetUpdaterId();

            try { UpdaterRegistry.UnregisterUpdater(_uid); } catch { }
            UpdaterRegistry.RegisterUpdater(updater, true);
            _registered = true;

            RefreshTriggers(OverheadSettings.Default());
        }

        public static void RefreshTriggers(OverheadSettings settings)
        {
            if (!_registered) return;

            try { UpdaterRegistry.RemoveAllTriggers(_uid); } catch { }

            var cats = settings.SelectedCategories?.ToList()
                       ?? new List<BuiltInCategory> { BuiltInCategory.OST_Walls };

            ElementFilter filter = cats.Count == 1
                ? new ElementCategoryFilter(cats[0])
                : new LogicalOrFilter(cats.ConvertAll<ElementFilter>(c => new ElementCategoryFilter(c)));

            UpdaterRegistry.AddTrigger(_uid, filter, Element.GetChangeTypeAny());
            UpdaterRegistry.AddTrigger(_uid, filter, Element.GetChangeTypeElementDeletion());
        }

        private static void RemoveProxiesForElementAllViews(Document doc, ElementId ownerId)
        {
            var plans = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(vp => vp.ViewType == ViewType.FloorPlan && !vp.IsTemplate)
                .ToList();

            foreach (var vp in plans)
                ProxyManager.RemoveProxies(vp, ownerId);
        }

        public static void Unregister(UIControlledApplication app)
        {
            if (app == null) return;

            var uid = new UpdaterId(app.ActiveAddInId, UpdaterGuid);

            try { UpdaterRegistry.RemoveAllTriggers(uid); } catch { }
            try { UpdaterRegistry.UnregisterUpdater(uid); } catch { }

            _registered = false;
        }
    }
}