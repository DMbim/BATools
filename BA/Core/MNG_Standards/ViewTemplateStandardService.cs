// File: BA.Core/Standards/ViewTemplateStandardService.cs
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using Color = Autodesk.Revit.DB.Color;

namespace BA.Core.Standards
{
    public static class ViewTemplateStandardService
    {
        // =========================
        // CAPTURE
        // =========================
        public static ViewTemplateStandardFile Capture(Document doc, Autodesk.Revit.DB.View template)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (!template.IsTemplate) throw new InvalidOperationException("Selected view is not a template.");

            var file = new ViewTemplateStandardFile
            {
                TemplateName = template.Name,
                SavedUtc = DateTime.UtcNow,
                Snapshot = new ViewTemplateSnapshot()
            };

            // 1) Non-controlled template parameter ids (Include/Exclude controls)
            file.Snapshot.NonControlledTemplateParamIds = template
                .GetNonControlledTemplateParameterIds()
                .Select(id => id.Value) // Revit 2026: long
                .OrderBy(x => x)
                .ToList();

            // 2) All view parameters
            file.Snapshot.Parameters = CaptureAllParameters(template);

            // 3) Categories (hidden + overrides)
            foreach (var cat in EnumerateAllCategories(doc))
            {
                try
                {
                    bool hidden = template.GetCategoryHidden(cat.Id);
                    var ogs = template.GetCategoryOverrides(cat.Id);

                    file.Snapshot.Categories[cat.Id.Value] = new CategoryOverrideSnapshot
                    {
                        CategoryId = cat.Id.Value,
                        CategoryName = cat.Name ?? "",
                        IsHidden = hidden,
                        Overrides = CaptureOgsSafe(ogs)
                    };
                }
                catch
                {
                    // not applicable for this template type -> ignore
                }
            }

            // 4) Filters (order + visibility + overrides)
            var filterIds = template.GetFilters().ToList();

            file.Snapshot.FilterOrder = filterIds
                .Select(id => doc.GetElement(id) as ParameterFilterElement)
                .Where(pfe => pfe != null)
                .Select(pfe => pfe!.Name)
                .ToList();

            foreach (var fid in filterIds)
            {
                var pfe = doc.GetElement(fid) as ParameterFilterElement;
                if (pfe == null) continue;

                bool vis = true;
                try { vis = template.GetFilterVisibility(fid); } catch { }

                var ogs = template.GetFilterOverrides(fid);

                file.Snapshot.Filters[pfe.Name] = new FilterOverrideSnapshot
                {
                    FilterName = pfe.Name,
                    IsVisible = vis,
                    Overrides = CaptureOgsSafe(ogs)
                };
            }

            // 5) Worksets
            try
            {
                var wsets = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset)
                    .ToWorksets();

                foreach (var ws in wsets)
                {
                    var vis = template.GetWorksetVisibility(ws.Id);
                    file.Snapshot.WorksetVisibility[(long)ws.Id.IntegerValue] = (int)vis;
                }
            }
            catch
            {
                // ignore if not supported in this view type
            }

            return file;
        }

        // =========================
        // APPLY (full standard)
        // =========================
        public static ApplyResult Apply(Document doc, Autodesk.Revit.DB.View template, ViewTemplateStandardFile standard)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (standard == null) throw new ArgumentNullException(nameof(standard));

            var res = new ApplyResult();

            // 1) Include/Exclude controls
            try
            {
                var ids = standard.Snapshot.NonControlledTemplateParamIds
                    .Select(ToElementId)
                    .Where(id => id != ElementId.InvalidElementId)
                    .ToList();

                template.SetNonControlledTemplateParameterIds(ids);
                res.AppliedNonControlledIds = true;
            }
            catch { }

            // 2) Parameters
            var currentParamById = new Dictionary<long, Parameter>();
            foreach (Parameter p in template.Parameters)
            {
                if (p == null) continue;
                var key = p.Id.Value;
                if (!currentParamById.ContainsKey(key))
                    currentParamById.Add(key, p);
            }

            foreach (var sp in standard.Snapshot.Parameters)
            {
                if (!currentParamById.TryGetValue(sp.ParamId, out var p)) continue;
                if (p.IsReadOnly) { res.SkippedReadOnlyParams++; continue; }

                if (TrySetParameter(p, sp))
                    res.AppliedParams++;
                else
                    res.SkippedParams++;
            }

            // 3) Categories
            foreach (var stdCat in standard.Snapshot.Categories.Values)
            {
                var cat = Category.GetCategory(doc, ToElementId(stdCat.CategoryId));
                if (cat == null) { res.MissingCategories.Add(stdCat.CategoryName); continue; }

                try
                {
                    template.SetCategoryHidden(cat.Id, stdCat.IsHidden);
                    template.SetCategoryOverrides(cat.Id, BuildOgs(stdCat.Overrides));
                    res.AppliedCategories++;
                }
                catch
                {
                    res.SkippedCategories.Add(cat.Name ?? stdCat.CategoryName);
                }
            }

            // 4) Filters: order + per-filter states
            var curFiltersByName = GetCurrentFiltersByName(doc, template);

            // desired order from standard
            var desiredOrder = new List<ElementId>();
            foreach (var fname in standard.Snapshot.FilterOrder)
                if (curFiltersByName.TryGetValue(fname, out var fid))
                    desiredOrder.Add(fid);

            // append any filters not present in standard
            foreach (var kv in curFiltersByName)
                if (!desiredOrder.Contains(kv.Value))
                    desiredOrder.Add(kv.Value);

            try
            {
                ViewFilterOrderUtils.ReorderFiltersPreserveStates(template, desiredOrder);
                res.AppliedFilterOrder = true;
            }
            catch { }

            foreach (var stdFilter in standard.Snapshot.Filters.Values)
            {
                if (!curFiltersByName.TryGetValue(stdFilter.FilterName, out var fid))
                {
                    res.MissingFilters.Add(stdFilter.FilterName);
                    continue;
                }

                try
                {
                    template.SetFilterVisibility(fid, stdFilter.IsVisible);
                    template.SetFilterOverrides(fid, BuildOgs(stdFilter.Overrides));
                    res.AppliedFilters++;
                }
                catch
                {
                    res.SkippedFilters.Add(stdFilter.FilterName);
                }
            }

            // 5) Worksets
            if (standard.Snapshot.WorksetVisibility.Count > 0)
            {
                try
                {
                    var wsets = new FilteredWorksetCollector(doc)
                        .OfKind(WorksetKind.UserWorkset)
                        .ToWorksets();

                    var byKey = wsets.ToDictionary(x => (long)x.Id.IntegerValue, x => x);

                    foreach (var kv in standard.Snapshot.WorksetVisibility)
                    {
                        if (!byKey.TryGetValue(kv.Key, out var ws)) continue;

                        try
                        {
                            template.SetWorksetVisibility(ws.Id, (WorksetVisibility)kv.Value);
                            res.AppliedWorksets++;
                        }
                        catch { res.SkippedWorksets++; }
                    }
                }
                catch { }
            }

            return res;
        }

        // =========================
        // COMPARE (for UI)
        // =========================
        public static List<TemplateDiffRow> Compare(Document doc, Autodesk.Revit.DB.View template, ViewTemplateStandardFile standard)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (standard == null) throw new ArgumentNullException(nameof(standard));

            var rows = new List<TemplateDiffRow>();

            // Filter order
            try
            {
                var currentOrder = template.GetFilters()
                    .Select(id => doc.GetElement(id) as ParameterFilterElement)
                    .Where(pfe => pfe != null)
                    .Select(pfe => pfe!.Name)
                    .ToList();

                bool same = SequenceEqualIgnoreCase(standard.Snapshot.FilterOrder, currentOrder);

                rows.Add(new TemplateDiffRow
                {
                    Scope = "FilterOrder",
                    Name = template.Name,
                    Property = "Order",
                    StandardValue = string.Join(" | ", standard.Snapshot.FilterOrder),
                    CurrentValue = string.Join(" | ", currentOrder),
                    IsMismatch = !same,
                    TargetKey = "FilterOrder"
                });
            }
            catch { }

            // Categories
            foreach (var stdCat in standard.Snapshot.Categories.Values.OrderBy(x => x.CategoryName))
            {
                var currentCat = Category.GetCategory(doc, ToElementId(stdCat.CategoryId));
                if (currentCat == null)
                {
                    rows.Add(new TemplateDiffRow
                    {
                        Scope = "Category",
                        Name = stdCat.CategoryName,
                        Property = "(missing)",
                        StandardValue = "Exists in standard",
                        CurrentValue = "Missing in project",
                        IsMismatch = true,
                        TargetId = stdCat.CategoryId
                    });
                    continue;
                }

                bool currentHidden;
                OverrideGraphicSettings currentOgs;

                try
                {
                    currentHidden = template.GetCategoryHidden(currentCat.Id);
                    currentOgs = template.GetCategoryOverrides(currentCat.Id);
                }
                catch
                {
                    rows.Add(new TemplateDiffRow
                    {
                        Scope = "Category",
                        Name = currentCat.Name,
                        Property = "(not applicable)",
                        StandardValue = "Captured",
                        CurrentValue = "Cannot read here",
                        IsMismatch = true,
                        TargetId = stdCat.CategoryId
                    });
                    continue;
                }

                AddDiff(rows, "Category", currentCat.Name, "Hidden",
                    stdCat.IsHidden.ToString(), currentHidden.ToString(),
                    targetId: stdCat.CategoryId,
                    alwaysAdd: true);

                CompareOgsDiffOnly(rows, "Category", currentCat.Name,
                    stdCat.Overrides, CaptureOgsSafe(currentOgs),
                    targetId: stdCat.CategoryId);
            }

            // Filters
            var curFiltersByName = GetCurrentFiltersByName(doc, template);
            foreach (var std in standard.Snapshot.Filters.Values.OrderBy(x => x.FilterName))
            {
                if (!curFiltersByName.TryGetValue(std.FilterName, out var fid))
                {
                    rows.Add(new TemplateDiffRow
                    {
                        Scope = "Filter",
                        Name = std.FilterName,
                        Property = "(missing)",
                        StandardValue = "Exists in standard",
                        CurrentValue = "Missing in template/project",
                        IsMismatch = true,
                        TargetKey = std.FilterName
                    });
                    continue;
                }

                // visibility
                try
                {
                    var curVis = template.GetFilterVisibility(fid);
                    AddDiff(rows, "Filter", std.FilterName, "Visible",
                        std.IsVisible.ToString(), curVis.ToString(),
                        targetKey: std.FilterName,
                        alwaysAdd: true);
                }
                catch { }

                // overrides
                try
                {
                    var curOgs = template.GetFilterOverrides(fid);
                    CompareOgsDiffOnly(rows, "Filter", std.FilterName,
                        std.Overrides, CaptureOgsSafe(curOgs),
                        targetKey: std.FilterName);
                }
                catch { }
            }

            // Worksets (diff only)
            if (standard.Snapshot.WorksetVisibility.Count > 0)
            {
                try
                {
                    var wsets = new FilteredWorksetCollector(doc)
                        .OfKind(WorksetKind.UserWorkset)
                        .ToWorksets();

                    var currentByKey = wsets.ToDictionary(x => (long)x.Id.IntegerValue, x => x);

                    foreach (var kv in standard.Snapshot.WorksetVisibility.OrderBy(k => k.Key))
                    {
                        if (!currentByKey.TryGetValue(kv.Key, out var ws))
                        {
                            rows.Add(new TemplateDiffRow
                            {
                                Scope = "Workset",
                                Name = $"#{kv.Key}",
                                Property = "(missing workset)",
                                StandardValue = ((WorksetVisibility)kv.Value).ToString(),
                                CurrentValue = "Missing",
                                IsMismatch = true,
                                TargetId = kv.Key
                            });
                            continue;
                        }

                        var curVis = template.GetWorksetVisibility(ws.Id);
                        var stdVis = (WorksetVisibility)kv.Value;

                        if (curVis != stdVis)
                        {
                            rows.Add(new TemplateDiffRow
                            {
                                Scope = "Workset",
                                Name = ws.Name,
                                Property = "Visibility",
                                StandardValue = stdVis.ToString(),
                                CurrentValue = curVis.ToString(),
                                IsMismatch = true,
                                TargetId = kv.Key
                            });
                        }
                    }
                }
                catch { }
            }

            return rows;
        }

        // =========================
        // APPLY FIXES (selected rows)
        // =========================
        public sealed class FixResult
        {
            public int FixedCategories { get; set; }
            public int FixedParameters { get; set; }
            public int FixedFilters { get; set; }
            public int FixedWorksets { get; set; }
            public int FixedFilterOrder { get; set; }
            public List<string> MissingTargets { get; } = new();
        }

        public static FixResult ApplyFixes(Document doc, Autodesk.Revit.DB.View template, ViewTemplateStandardFile standard, IList<TemplateDiffRow> selectedRows)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (standard == null) throw new ArgumentNullException(nameof(standard));
            if (selectedRows == null) throw new ArgumentNullException(nameof(selectedRows));

            var res = new FixResult();

            var categoryIds = selectedRows
                .Where(r => r.Scope == "Category" && r.TargetId.HasValue)
                .Select(r => r.TargetId!.Value)
                .Distinct()
                .ToList();

            var filterNames = selectedRows
                .Where(r => r.Scope == "Filter" && !string.IsNullOrWhiteSpace(r.TargetKey))
                .Select(r => r.TargetKey!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool fixOrder = selectedRows.Any(r => r.Scope == "FilterOrder");

            var worksetKeys = selectedRows
                .Where(r => r.Scope == "Workset" && r.TargetId.HasValue)
                .Select(r => r.TargetId!.Value)
                .Distinct()
                .ToList();

            // Categories
            foreach (var cid in categoryIds)
            {
                if (!standard.Snapshot.Categories.TryGetValue(cid, out var stdCat))
                {
                    res.MissingTargets.Add($"CategoryId {cid} (missing in standard)");
                    continue;
                }

                var cat = Category.GetCategory(doc, ToElementId(cid));
                if (cat == null)
                {
                    res.MissingTargets.Add($"Category missing: {stdCat.CategoryName}");
                    continue;
                }

                try
                {
                    template.SetCategoryHidden(cat.Id, stdCat.IsHidden);
                    template.SetCategoryOverrides(cat.Id, BuildOgs(stdCat.Overrides));
                    res.FixedCategories++;
                }
                catch
                {
                    res.MissingTargets.Add($"Category apply failed: {stdCat.CategoryName}");
                }
            }

            // Filters
            var curFiltersByName = GetCurrentFiltersByName(doc, template);

            foreach (var fname in filterNames)
            {
                if (!standard.Snapshot.Filters.TryGetValue(fname, out var stdFilter))
                {
                    res.MissingTargets.Add($"Filter missing in standard: {fname}");
                    continue;
                }

                if (!curFiltersByName.TryGetValue(fname, out var fid))
                {
                    res.MissingTargets.Add($"Filter missing in template: {fname}");
                    continue;
                }

                try
                {
                    template.SetFilterVisibility(fid, stdFilter.IsVisible);
                    template.SetFilterOverrides(fid, BuildOgs(stdFilter.Overrides));
                    res.FixedFilters++;
                }
                catch
                {
                    res.MissingTargets.Add($"Filter apply failed: {fname}");
                }
            }

            // Filter order
            if (fixOrder && standard.Snapshot.FilterOrder.Count > 0)
            {
                try
                {
                    var desired = new List<ElementId>();
                    foreach (var fname in standard.Snapshot.FilterOrder)
                        if (curFiltersByName.TryGetValue(fname, out var fid))
                            desired.Add(fid);

                    foreach (var kv in curFiltersByName)
                        if (!desired.Contains(kv.Value))
                            desired.Add(kv.Value);

                    ViewFilterOrderUtils.ReorderFiltersPreserveStates(template, desired);
                    res.FixedFilterOrder = 1;
                }
                catch
                {
                    res.MissingTargets.Add("Filter order reorder failed.");
                }
            }

            // Worksets
            if (worksetKeys.Count > 0 && standard.Snapshot.WorksetVisibility.Count > 0)
            {
                try
                {
                    var wsets = new FilteredWorksetCollector(doc)
                        .OfKind(WorksetKind.UserWorkset)
                        .ToWorksets();

                    var byKey = wsets.ToDictionary(x => (long)x.Id.IntegerValue, x => x);

                    foreach (var key in worksetKeys)
                    {
                        if (!standard.Snapshot.WorksetVisibility.TryGetValue(key, out var stdVal))
                        {
                            res.MissingTargets.Add($"Workset key missing in standard: {key}");
                            continue;
                        }

                        if (!byKey.TryGetValue(key, out var ws))
                        {
                            res.MissingTargets.Add($"Workset missing in model (key): {key}");
                            continue;
                        }

                        try
                        {
                            template.SetWorksetVisibility(ws.Id, (WorksetVisibility)stdVal);
                            res.FixedWorksets++;
                        }
                        catch
                        {
                            res.MissingTargets.Add($"Workset apply failed: {ws.Name}");
                        }
                    }
                }
                catch
                {
                    res.MissingTargets.Add("Workset apply failed (collector).");
                }
            }

            return res;
        }

        // =========================
        // RESULT DTO
        // =========================
        public sealed class ApplyResult
        {
            public bool AppliedNonControlledIds { get; set; }

            public int AppliedParams { get; set; }
            public int SkippedParams { get; set; }
            public int SkippedReadOnlyParams { get; set; }

            public int AppliedCategories { get; set; }
            public List<string> MissingCategories { get; } = new();
            public List<string> SkippedCategories { get; } = new();

            public bool AppliedFilterOrder { get; set; }
            public int AppliedFilters { get; set; }
            public List<string> MissingFilters { get; } = new();
            public List<string> SkippedFilters { get; } = new();

            public int AppliedWorksets { get; set; }
            public int SkippedWorksets { get; set; }

            // aliases for UI
            public int AppliedCategoryCount => AppliedCategories;
            public int AppliedFilterCount => AppliedFilters;
        }

        // =========================
        // HELPERS
        // =========================

        private static Dictionary<string, ElementId> GetCurrentFiltersByName(Document doc, Autodesk.Revit.DB.View template)
        {
            var dict = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);

            foreach (var fid in template.GetFilters())
            {
                var pfe = doc.GetElement(fid) as ParameterFilterElement;
                if (pfe == null) continue;
                if (!dict.ContainsKey(pfe.Name))
                    dict.Add(pfe.Name, fid);
            }

            return dict;
        }

        private static List<ViewParamSnapshot> CaptureAllParameters(Autodesk.Revit.DB.View v)
        {
            var list = new List<ViewParamSnapshot>();

            foreach (Parameter p in v.Parameters)
            {
                if (p?.Definition == null) continue;

                var snap = new ViewParamSnapshot
                {
                    ParamId = p.Id.Value,
                    Name = p.Definition.Name ?? "",
                    StorageType = (int)p.StorageType,
                    DisplayValue = SafeValueString(p)
                };

                switch (p.StorageType)
                {
                    case StorageType.String: snap.StringValue = p.AsString(); break;
                    case StorageType.Integer: snap.IntValue = p.AsInteger(); break;
                    case StorageType.Double: snap.DoubleValue = p.AsDouble(); break;
                    case StorageType.ElementId:
                        var id = p.AsElementId();
                        snap.ElementIdValue = id != null ? id.Value : null;
                        break;
                }

                list.Add(snap);
            }

            return list;
        }

        private static string SafeValueString(Parameter p)
        {
            try { return p.AsValueString() ?? ""; }
            catch { return ""; }
        }

        private static bool TrySetParameter(Parameter p, ViewParamSnapshot sp)
        {
            try
            {
                switch ((StorageType)sp.StorageType)
                {
                    case StorageType.String:
                        return p.Set(sp.StringValue ?? "");
                    case StorageType.Integer:
                        return sp.IntValue.HasValue && p.Set(sp.IntValue.Value);
                    case StorageType.Double:
                        return sp.DoubleValue.HasValue && p.Set(sp.DoubleValue.Value);
                    case StorageType.ElementId:
                        return sp.ElementIdValue.HasValue && p.Set(ToElementId(sp.ElementIdValue.Value));
                    default:
                        return false;
                }
            }
            catch { return false; }
        }

        private static IEnumerable<Category> EnumerateAllCategories(Document doc)
        {
            var seen = new HashSet<long>();

            foreach (Category root in doc.Settings.Categories)
            {
                if (root == null) continue;

                foreach (var c in FlattenCategoriesSafe(root))
                {
                    if (c?.Id == null) continue;
                    if (seen.Add(c.Id.Value))
                        yield return c;
                }
            }
        }

        private static IEnumerable<Category> FlattenCategoriesSafe(Category root)
        {
            var stack = new Stack<Category>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var c = stack.Pop();
                yield return c;

                CategoryNameMap? subs = null;
                try { subs = c.SubCategories; } catch { subs = null; }
                if (subs == null) continue;

                foreach (Category sub in subs)
                    if (sub != null) stack.Push(sub);
            }
        }

        private static void AddDiff(
            List<TemplateDiffRow> rows,
            string scope, string name, string prop,
            string stdVal, string curVal,
            long? targetId = null,
            string? targetKey = null,
            bool alwaysAdd = false)
        {
            bool mismatch = !string.Equals(stdVal ?? "", curVal ?? "", StringComparison.OrdinalIgnoreCase);
            if (!alwaysAdd && !mismatch) return;

            rows.Add(new TemplateDiffRow
            {
                Scope = scope,
                Name = name,
                Property = prop,
                StandardValue = stdVal ?? "",
                CurrentValue = curVal ?? "",
                IsMismatch = mismatch,
                TargetId = targetId,
                TargetKey = targetKey
            });
        }

        private static void CompareOgsDiffOnly(
            List<TemplateDiffRow> rows,
            string scope,
            string name,
            GraphicOverrideSnapshot std,
            GraphicOverrideSnapshot cur,
            long? targetId = null,
            string? targetKey = null)
        {
            AddOgs(rows, scope, name, "ProjectionLineColor", Val(std.ProjectionLineColor), Val(cur.ProjectionLineColor), targetId, targetKey);
            AddOgs(rows, scope, name, "ProjectionLineWeight", Val(std.ProjectionLineWeight), Val(cur.ProjectionLineWeight), targetId, targetKey);
            AddOgs(rows, scope, name, "ProjectionLinePatternId", Val(std.ProjectionLinePatternId), Val(cur.ProjectionLinePatternId), targetId, targetKey);

            AddOgs(rows, scope, name, "CutLineColor", Val(std.CutLineColor), Val(cur.CutLineColor), targetId, targetKey);
            AddOgs(rows, scope, name, "CutLineWeight", Val(std.CutLineWeight), Val(cur.CutLineWeight), targetId, targetKey);
            AddOgs(rows, scope, name, "CutLinePatternId", Val(std.CutLinePatternId), Val(cur.CutLinePatternId), targetId, targetKey);

            AddOgs(rows, scope, name, "SurfaceForegroundPatternId", Val(std.SurfaceForegroundPatternId), Val(cur.SurfaceForegroundPatternId), targetId, targetKey);
            AddOgs(rows, scope, name, "SurfaceForegroundPatternColor", Val(std.SurfaceForegroundPatternColor), Val(cur.SurfaceForegroundPatternColor), targetId, targetKey);
            AddOgs(rows, scope, name, "SurfaceBackgroundPatternId", Val(std.SurfaceBackgroundPatternId), Val(cur.SurfaceBackgroundPatternId), targetId, targetKey);
            AddOgs(rows, scope, name, "SurfaceBackgroundPatternColor", Val(std.SurfaceBackgroundPatternColor), Val(cur.SurfaceBackgroundPatternColor), targetId, targetKey);

            AddOgs(rows, scope, name, "CutForegroundPatternId", Val(std.CutForegroundPatternId), Val(cur.CutForegroundPatternId), targetId, targetKey);
            AddOgs(rows, scope, name, "CutForegroundPatternColor", Val(std.CutForegroundPatternColor), Val(cur.CutForegroundPatternColor), targetId, targetKey);
            AddOgs(rows, scope, name, "CutBackgroundPatternId", Val(std.CutBackgroundPatternId), Val(cur.CutBackgroundPatternId), targetId, targetKey);
            AddOgs(rows, scope, name, "CutBackgroundPatternColor", Val(std.CutBackgroundPatternColor), Val(cur.CutBackgroundPatternColor), targetId, targetKey);

            AddOgs(rows, scope, name, "Transparency", Val(std.Transparency), Val(cur.Transparency), targetId, targetKey);
            AddOgs(rows, scope, name, "Halftone", Val(std.Halftone), Val(cur.Halftone), targetId, targetKey);
        }

        private static void AddOgs(
            List<TemplateDiffRow> rows,
            string scope, string name, string prop,
            string stdVal, string curVal,
            long? targetId,
            string? targetKey)
        {
            if (!string.Equals(stdVal, curVal, StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(new TemplateDiffRow
                {
                    Scope = scope,
                    Name = name,
                    Property = prop,
                    StandardValue = stdVal,
                    CurrentValue = curVal,
                    IsMismatch = true,
                    TargetId = targetId,
                    TargetKey = targetKey
                });
            }
        }

        private static string Val(RgbColor? c) => c.HasValue ? c.Value.ToString() : "—";
        private static string Val(long? v) => v.HasValue ? v.Value.ToString() : "—";
        private static string Val(int? v) => v.HasValue ? v.Value.ToString() : "—";
        private static string Val(bool? v) => v.HasValue ? v.Value.ToString() : "—";

        private static bool SequenceEqualIgnoreCase(IList<string> a, IList<string> b)
        {
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i] ?? "", b[i] ?? "", StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        // ---------- OGS capture/build (SAFE COLOR READS) ----------

        private static GraphicOverrideSnapshot CaptureOgsSafe(OverrideGraphicSettings ogs)
        {
            var s = new GraphicOverrideSnapshot();

            if (TryReadRgb(ogs.ProjectionLineColor, out var plc)) s.ProjectionLineColor = plc;
            if (ogs.ProjectionLineWeight > 0) s.ProjectionLineWeight = ogs.ProjectionLineWeight;
            if (IsValidId(ogs.ProjectionLinePatternId)) s.ProjectionLinePatternId = ogs.ProjectionLinePatternId.Value;

            if (TryReadRgb(ogs.CutLineColor, out var clc)) s.CutLineColor = clc;
            if (ogs.CutLineWeight > 0) s.CutLineWeight = ogs.CutLineWeight;
            if (IsValidId(ogs.CutLinePatternId)) s.CutLinePatternId = ogs.CutLinePatternId.Value;

            if (IsValidId(ogs.SurfaceForegroundPatternId)) s.SurfaceForegroundPatternId = ogs.SurfaceForegroundPatternId.Value;
            if (TryReadRgb(ogs.SurfaceForegroundPatternColor, out var sfpc)) s.SurfaceForegroundPatternColor = sfpc;

            if (IsValidId(ogs.SurfaceBackgroundPatternId)) s.SurfaceBackgroundPatternId = ogs.SurfaceBackgroundPatternId.Value;
            if (TryReadRgb(ogs.SurfaceBackgroundPatternColor, out var sbpc)) s.SurfaceBackgroundPatternColor = sbpc;

            if (IsValidId(ogs.CutForegroundPatternId)) s.CutForegroundPatternId = ogs.CutForegroundPatternId.Value;
            if (TryReadRgb(ogs.CutForegroundPatternColor, out var cfpc)) s.CutForegroundPatternColor = cfpc;

            if (IsValidId(ogs.CutBackgroundPatternId)) s.CutBackgroundPatternId = ogs.CutBackgroundPatternId.Value;
            if (TryReadRgb(ogs.CutBackgroundPatternColor, out var cbpc)) s.CutBackgroundPatternColor = cbpc;

            if (ogs.Transparency != 0) s.Transparency = ogs.Transparency;
            if (ogs.Halftone) s.Halftone = true;

            return s;
        }

        private static bool TryReadRgb(Color c, out RgbColor rgb)
        {
            rgb = default;
            if (c == null) return false;

            try
            {
                int r = c.Red;
                int g = c.Green;
                int b = c.Blue;

                if (r < 0 || g < 0 || b < 0) return false;
                if (r > 255 || g > 255 || b > 255) return false;

                rgb = new RgbColor((byte)r, (byte)g, (byte)b);
                return true;
            }
            catch
            {
                // This is the exact fix for: "invalid or uninitialized color."
                return false;
            }
        }

        private static OverrideGraphicSettings BuildOgs(GraphicOverrideSnapshot s)
        {
            var ogs = new OverrideGraphicSettings();

            if (s.ProjectionLineColor.HasValue) ogs.SetProjectionLineColor(ToRevit(s.ProjectionLineColor.Value));
            if (s.ProjectionLineWeight.HasValue) ogs.SetProjectionLineWeight(s.ProjectionLineWeight.Value);
            if (s.ProjectionLinePatternId.HasValue) ogs.SetProjectionLinePatternId(ToElementId(s.ProjectionLinePatternId.Value));

            if (s.CutLineColor.HasValue) ogs.SetCutLineColor(ToRevit(s.CutLineColor.Value));
            if (s.CutLineWeight.HasValue) ogs.SetCutLineWeight(s.CutLineWeight.Value);
            if (s.CutLinePatternId.HasValue) ogs.SetCutLinePatternId(ToElementId(s.CutLinePatternId.Value));

            if (s.SurfaceForegroundPatternId.HasValue) ogs.SetSurfaceForegroundPatternId(ToElementId(s.SurfaceForegroundPatternId.Value));
            if (s.SurfaceForegroundPatternColor.HasValue) ogs.SetSurfaceForegroundPatternColor(ToRevit(s.SurfaceForegroundPatternColor.Value));

            if (s.SurfaceBackgroundPatternId.HasValue) ogs.SetSurfaceBackgroundPatternId(ToElementId(s.SurfaceBackgroundPatternId.Value));
            if (s.SurfaceBackgroundPatternColor.HasValue) ogs.SetSurfaceBackgroundPatternColor(ToRevit(s.SurfaceBackgroundPatternColor.Value));

            if (s.CutForegroundPatternId.HasValue) ogs.SetCutForegroundPatternId(ToElementId(s.CutForegroundPatternId.Value));
            if (s.CutForegroundPatternColor.HasValue) ogs.SetCutForegroundPatternColor(ToRevit(s.CutForegroundPatternColor.Value));

            if (s.CutBackgroundPatternId.HasValue) ogs.SetCutBackgroundPatternId(ToElementId(s.CutBackgroundPatternId.Value));
            if (s.CutBackgroundPatternColor.HasValue) ogs.SetCutBackgroundPatternColor(ToRevit(s.CutBackgroundPatternColor.Value));

            if (s.Transparency.HasValue) ogs.SetSurfaceTransparency(s.Transparency.Value);
            if (s.Halftone.HasValue) ogs.SetHalftone(s.Halftone.Value);

            return ogs;
        }

        private static bool IsValidId(ElementId id) =>
            id != null && id != ElementId.InvalidElementId && id.Value > 0;

        private static Color ToRevit(RgbColor c) => new Color(c.R, c.G, c.B);

        // Revit ElementId ctor takes int; Revit 2026 exposes Value as long.
        private static ElementId ToElementId(long v)
        {
            if (v <= 0) return ElementId.InvalidElementId;
            if (v > int.MaxValue) throw new OverflowException($"ElementId value too large for int: {v}");
            return new ElementId((int)v);
        }

        /* Service error: Unknown */

    }
}
