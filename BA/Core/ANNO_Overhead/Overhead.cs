using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BA.Core.Overhead
{
    public sealed class OverheadAnalyzer
    {
        private readonly Document _doc;
        private readonly ViewPlan _view;
        private readonly OverheadSettings _settings;

        public OverheadAnalyzer(Document doc, ViewPlan view, OverheadSettings settings)
        {
            _doc = doc;
            _view = view;
            _settings = settings ?? OverheadSettings.Default();
            _settings.Normalize();
        }

        public AnalysisResult Run()
        {
            var gsOverhead = LineStyleLookup.FindOverhead(_doc);
            if (gsOverhead == null)
                return new AnalysisResult { OverriddenCount = 0, CutZmm = 0, TopZmm = 0 };

            var ogs = StyleMapper.BuildOGSFrom(gsOverhead);

            var (cutZ, topZ) = ViewRangeResolver.ResolveCutTopZ(_doc, _view, _settings);
            double eps = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);

            var cats = _settings.SelectedCategories?.ToList() ?? new List<BuiltInCategory> { BuiltInCategory.OST_Walls };
            var modelFilter = new ElementMulticategoryFilter(cats);

            var elems = new FilteredElementCollector(_doc, _view.Id)
                .WherePasses(modelFilter)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model)
                .ToList();
            if (!_settings.Enabled)
                return new AnalysisResult { OverriddenCount = 0, CutZmm = 0, TopZmm = 0 };
            var overridden = new List<ElementId>();
            foreach (var e in elems)
            {
                if (e.IsHidden(_view) || (e.Category != null && _view.GetCategoryHidden(e.Category.Id)))
                    continue;

                var bb = e.get_BoundingBox(null);
                if (bb == null) continue;

                if (IsTinyXY(bb, _settings.TinyThresholdMm))
                    continue;

                bool cutRequired = IsCutRequiredCategory(e.Category);

                bool inOverheadBand = (bb.Min.Z >= cutZ + eps) && (bb.Min.Z <= topZ);
                bool aboveCut = bb.Max.Z > (cutZ + eps);
                bool aboveTop = bb.Min.Z > topZ;

                bool shouldOverhead = (cutRequired && inOverheadBand) || (aboveCut && aboveTop);

                if (shouldOverhead)
                {
                    _view.SetElementOverrides(e.Id, ogs);
                    overridden.Add(e.Id);
                }
                else
                {
                    if (OverheadStateStore.WasOverridden(_doc, _view.Id, e.Id))
                        _view.SetElementOverrides(e.Id, new OverrideGraphicSettings());
                }
            }

            OverheadStateStore.SaveLastRun(_doc, _view.Id, overridden);

            return new AnalysisResult
            {
                OverriddenCount = overridden.Count,
                CutZmm = UnitUtils.ConvertFromInternalUnits(cutZ, UnitTypeId.Millimeters),
                TopZmm = UnitUtils.ConvertFromInternalUnits(topZ, UnitTypeId.Millimeters)
            };
        }

        private static bool IsCutRequiredCategory(Category cat)
        {
            if (cat == null) return false;
            return cat.Id == new ElementId(BuiltInCategory.OST_Walls)
                   || cat.Id == new ElementId(BuiltInCategory.OST_StructuralColumns);
        }

        private static bool IsTinyXY(BoundingBoxXYZ bb, double tinyMm)
        {
            if (bb == null) return true;
            double tinyFt = UnitUtils.ConvertToInternalUnits(tinyMm <= 0 ? 0 : tinyMm, UnitTypeId.Millimeters);
            return (bb.Max.X - bb.Min.X) < tinyFt && (bb.Max.Y - bb.Min.Y) < tinyFt;
        }
    }

    public sealed class AnalysisResult
    {
        public int OverriddenCount { get; set; }
        public double CutZmm { get; set; }
        public double TopZmm { get; set; }
    }
}
