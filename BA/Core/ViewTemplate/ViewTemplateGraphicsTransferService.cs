using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using View = Autodesk.Revit.DB.View;

namespace BA.Core.ViewTemplates
{
    public static class ViewTemplateGraphicsTransferService
    {
        public static List<CategoryTransferItem> GetTransferableModelCategories(Document doc, ElementId sourceTemplateId)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceTemplateId == null || sourceTemplateId == ElementId.InvalidElementId)
                throw new ArgumentException("Invalid source template id.", nameof(sourceTemplateId));

            View sourceTemplate = doc.GetElement(sourceTemplateId) as View
                ?? throw new InvalidOperationException("Source template not found.");

            if (!sourceTemplate.IsTemplate)
                throw new InvalidOperationException("Selected source view is not a template.");

            List<CategoryTransferItem> result = new List<CategoryTransferItem>();

            Categories categories = doc.Settings.Categories;
            foreach (Category cat in categories)
            {
                if (cat == null)
                    continue;

                if (cat.CategoryType != CategoryType.Model)
                    continue;

                if (cat.Id == null || cat.Id == ElementId.InvalidElementId)
                    continue;

                // Skip tags, internal/unhelpful categories, and categories that usually
                // do not behave well for ordinary VG category transfer.
                if (!IsUsableTopLevelModelCategory(cat))
                    continue;

                // Only include categories that can be queried on the source view without throwing.
                if (!CanReadCategoryGraphics(sourceTemplate, cat.Id))
                    continue;

                result.Add(new CategoryTransferItem(cat.Id, cat.Name, cat.CategoryType));
            }

            return result
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static ApplyCategoryGraphicsResult ApplySelectedModelCategoryGraphics(
            Document doc,
            ElementId sourceTemplateId,
            ICollection<ElementId> targetTemplateIds,
            ICollection<ElementId> selectedCategoryIds,
            bool copyVisibility,
            bool copyProjectionOverrides,
            bool copyCutOverrides)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceTemplateId == null || sourceTemplateId == ElementId.InvalidElementId)
                throw new ArgumentException("Invalid source template id.", nameof(sourceTemplateId));
            if (targetTemplateIds == null) throw new ArgumentNullException(nameof(targetTemplateIds));
            if (selectedCategoryIds == null) throw new ArgumentNullException(nameof(selectedCategoryIds));

            View sourceTemplate = doc.GetElement(sourceTemplateId) as View
                ?? throw new InvalidOperationException("Source template not found.");

            if (!sourceTemplate.IsTemplate)
                throw new InvalidOperationException("Selected source view is not a template.");

            var result = new ApplyCategoryGraphicsResult
            {
                SourceTemplateName = sourceTemplate.Name,
                RequestedTargets = targetTemplateIds.Count,
                RequestedCategories = selectedCategoryIds.Count
            };

            if (!copyVisibility && !copyProjectionOverrides && !copyCutOverrides)
            {
                result.Messages.Add("Nothing selected to copy. Enable visibility and/or graphic override options.");
                return result;
            }

            if (selectedCategoryIds.Count == 0)
            {
                result.Messages.Add("No categories were selected.");
                return result;
            }

            List<ElementId> effectiveCategoryIds = selectedCategoryIds
                .Where(id => id != null && id != ElementId.InvalidElementId)
                .Distinct(new ElementIdValueComparer())
                .ToList();

            using Transaction tx = new Transaction(doc, "Apply selected category graphics to templates");
            tx.Start();

            foreach (ElementId targetId in targetTemplateIds)
            {
                if (targetId == null || targetId == ElementId.InvalidElementId)
                {
                    result.SkippedTargets++;
                    result.Messages.Add("Skipped invalid target template id.");
                    continue;
                }

                if (targetId.Value == sourceTemplateId.Value)
                {
                    result.SkippedTargets++;
                    result.Messages.Add($"Skipped source template itself: {sourceTemplate.Name}");
                    continue;
                }

                View targetTemplate = doc.GetElement(targetId) as View;
                if (targetTemplate == null || !targetTemplate.IsTemplate)
                {
                    result.SkippedTargets++;
                    result.Messages.Add($"Skipped non-template target: {targetId.Value}");
                    continue;
                }

                if (targetTemplate.ViewType != sourceTemplate.ViewType)
                {
                    result.SkippedTargets++;
                    result.Messages.Add(
                        $"Skipped '{targetTemplate.Name}' because view type differs " +
                        $"({targetTemplate.ViewType} != {sourceTemplate.ViewType}).");
                    continue;
                }

                int appliedForThisTarget = 0;

                foreach (ElementId catId in effectiveCategoryIds)
                {
                    try
                    {
                        CopySingleCategoryGraphics(
                            sourceTemplate,
                            targetTemplate,
                            catId,
                            copyVisibility,
                            copyProjectionOverrides,
                            copyCutOverrides);

                        appliedForThisTarget++;
                    }
                    catch (Exception ex)
                    {
                        Category? cat = Category.GetCategory(doc, catId);
                        string catName = cat?.Name ?? $"Category {catId.Value}";
                        result.Messages.Add($"Failed category '{catName}' on '{targetTemplate.Name}': {ex.Message}");
                    }
                }

                if (appliedForThisTarget > 0)
                {
                    result.UpdatedTargets++;
                    result.AppliedCategoriesPerTarget = appliedForThisTarget;
                    result.Messages.Add(
                        $"Updated '{targetTemplate.Name}' with {appliedForThisTarget} category graphic setting(s).");
                }
                else
                {
                    result.SkippedTargets++;
                    result.Messages.Add($"No category graphics applied to '{targetTemplate.Name}'.");
                }
            }

            tx.Commit();
            return result;
        }

        private static void CopySingleCategoryGraphics(
            View sourceTemplate,
            View targetTemplate,
            ElementId categoryId,
            bool copyVisibility,
            bool copyProjectionOverrides,
            bool copyCutOverrides)
        {
            if (sourceTemplate == null) throw new ArgumentNullException(nameof(sourceTemplate));
            if (targetTemplate == null) throw new ArgumentNullException(nameof(targetTemplate));
            if (categoryId == null || categoryId == ElementId.InvalidElementId)
                throw new ArgumentException("Invalid category id.", nameof(categoryId));

            OverrideGraphicSettings sourceOgs = sourceTemplate.GetCategoryOverrides(categoryId);

            if (copyVisibility)
            {
                bool hidden = sourceTemplate.GetCategoryHidden(categoryId);
                targetTemplate.SetCategoryHidden(categoryId, hidden);
            }

            if (copyProjectionOverrides || copyCutOverrides)
            {
                OverrideGraphicSettings targetCurrent = targetTemplate.GetCategoryOverrides(categoryId);
                OverrideGraphicSettings merged = MergeOverrideGraphicSettings(
                    targetCurrent,
                    sourceOgs,
                    copyProjectionOverrides,
                    copyCutOverrides);

                targetTemplate.SetCategoryOverrides(categoryId, merged);
            }
        }

        private static OverrideGraphicSettings MergeOverrideGraphicSettings(
            OverrideGraphicSettings targetCurrent,
            OverrideGraphicSettings source,
            bool copyProjectionOverrides,
            bool copyCutOverrides)
        {
            if (targetCurrent == null) throw new ArgumentNullException(nameof(targetCurrent));
            if (source == null) throw new ArgumentNullException(nameof(source));

            OverrideGraphicSettings merged = CloneOverrideGraphicSettings(targetCurrent);

            if (copyProjectionOverrides)
            {
                merged.SetProjectionLineColor(source.ProjectionLineColor);
                merged.SetProjectionLinePatternId(source.ProjectionLinePatternId);
                merged.SetProjectionLineWeight(source.ProjectionLineWeight);

                merged.SetSurfaceForegroundPatternColor(source.SurfaceForegroundPatternColor);
                merged.SetSurfaceForegroundPatternId(source.SurfaceForegroundPatternId);
                merged.SetSurfaceForegroundPatternVisible(source.IsSurfaceForegroundPatternVisible);

                merged.SetSurfaceBackgroundPatternColor(source.SurfaceBackgroundPatternColor);
                merged.SetSurfaceBackgroundPatternId(source.SurfaceBackgroundPatternId);
                merged.SetSurfaceBackgroundPatternVisible(source.IsSurfaceBackgroundPatternVisible);

                merged.SetHalftone(source.Halftone);


                // If your Revit 2026 build exposes detail level here, keep it.
                // If not, remove this line.
                merged.SetDetailLevel(source.DetailLevel);
            }

            if (copyCutOverrides)
            {
                merged.SetCutLineColor(source.CutLineColor);
                merged.SetCutLinePatternId(source.CutLinePatternId);
                merged.SetCutLineWeight(source.CutLineWeight);

                merged.SetCutForegroundPatternColor(source.CutForegroundPatternColor);
                merged.SetCutForegroundPatternId(source.CutForegroundPatternId);
                merged.SetCutForegroundPatternVisible(source.IsCutForegroundPatternVisible);

                merged.SetCutBackgroundPatternColor(source.CutBackgroundPatternColor);
                merged.SetCutBackgroundPatternId(source.CutBackgroundPatternId);
                merged.SetCutBackgroundPatternVisible(source.IsCutBackgroundPatternVisible);
            }

            return merged;
        }

        private static OverrideGraphicSettings CloneOverrideGraphicSettings(OverrideGraphicSettings source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            OverrideGraphicSettings clone = new OverrideGraphicSettings();

            clone.SetProjectionLineColor(source.ProjectionLineColor);
            clone.SetProjectionLinePatternId(source.ProjectionLinePatternId);
            clone.SetProjectionLineWeight(source.ProjectionLineWeight);

            clone.SetCutLineColor(source.CutLineColor);
            clone.SetCutLinePatternId(source.CutLinePatternId);
            clone.SetCutLineWeight(source.CutLineWeight);

            clone.SetSurfaceForegroundPatternColor(source.SurfaceForegroundPatternColor);
            clone.SetSurfaceForegroundPatternId(source.SurfaceForegroundPatternId);
            clone.SetSurfaceForegroundPatternVisible(source.IsSurfaceForegroundPatternVisible);

            clone.SetSurfaceBackgroundPatternColor(source.SurfaceBackgroundPatternColor);
            clone.SetSurfaceBackgroundPatternId(source.SurfaceBackgroundPatternId);
            clone.SetSurfaceBackgroundPatternVisible(source.IsSurfaceBackgroundPatternVisible);

            clone.SetCutForegroundPatternColor(source.CutForegroundPatternColor);
            clone.SetCutForegroundPatternId(source.CutForegroundPatternId);
            clone.SetCutForegroundPatternVisible(source.IsCutForegroundPatternVisible);

            clone.SetCutBackgroundPatternColor(source.CutBackgroundPatternColor);
            clone.SetCutBackgroundPatternId(source.CutBackgroundPatternId);
            clone.SetCutBackgroundPatternVisible(source.IsCutBackgroundPatternVisible);

            clone.SetHalftone(source.Halftone);


            // If your build exposes this setter, keep it.
            // If compile fails here, remove this line.
            clone.SetDetailLevel(source.DetailLevel);

            return clone;
        }

        private static bool IsUsableTopLevelModelCategory(Category cat)
        {
            if (cat == null) return false;
            if (cat.Parent != null) return false; // top-level only for first implementation
            if (!cat.AllowsBoundParameters && !cat.CanAddSubcategory)
            {
                // not perfect, but helps skip many internal-ish categories
            }

            string name = cat.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.StartsWith("<")) return false;

            return true;
        }

        private static bool CanReadCategoryGraphics(View view, ElementId categoryId)
        {
            try
            {
                _ = view.GetCategoryHidden(categoryId);
                _ = view.GetCategoryOverrides(categoryId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private sealed class ElementIdValueComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId? x, ElementId? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x is null || y is null) return false;
                return x.Value == y.Value;
            }

            public int GetHashCode(ElementId obj)
            {
                return obj.Value.GetHashCode();
            }
        }
    }
}