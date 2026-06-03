using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using View = Autodesk.Revit.DB.View;

namespace BA.Core.ViewTemplates
{
    public static class ViewTemplateFilterTransferService
    {
        public static List<ViewFilterTransferItem> GetAppliedFilters(Document doc, ElementId sourceTemplateId)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceTemplateId == null || sourceTemplateId == ElementId.InvalidElementId)
                throw new ArgumentException("Invalid source template id.", nameof(sourceTemplateId));

            View sourceTemplate = doc.GetElement(sourceTemplateId) as View
                ?? throw new InvalidOperationException("Source template not found.");

            if (!sourceTemplate.IsTemplate)
                throw new InvalidOperationException("Selected source view is not a template.");

            List<ElementId> orderedIds = sourceTemplate.GetOrderedFilters().ToList();
            List<ViewFilterTransferItem> result = new List<ViewFilterTransferItem>();

            foreach (ElementId filterId in orderedIds)
            {
                if (filterId == null || filterId == ElementId.InvalidElementId)
                    continue;

                Element filter = doc.GetElement(filterId);
                if (filter == null)
                    continue;

                string typeName = filter.GetType().Name;
                result.Add(new ViewFilterTransferItem(filterId, filter.Name, typeName));
            }

            return result
                .OrderBy(x => orderedIds.FindIndex(id => id.Value == x.FilterId.Value))
                .ToList();
        }

        public static ApplyViewFiltersResult ApplySelectedFilters(
            Document doc,
            ElementId sourceTemplateId,
            ICollection<ElementId> targetTemplateIds,
            ICollection<ElementId> selectedFilterIds,
            bool copyEnabledState,
            bool copyVisibility,
            bool copyOverrides,
            bool preserveOrder)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceTemplateId == null || sourceTemplateId == ElementId.InvalidElementId)
                throw new ArgumentException("Invalid source template id.", nameof(sourceTemplateId));
            if (targetTemplateIds == null) throw new ArgumentNullException(nameof(targetTemplateIds));
            if (selectedFilterIds == null) throw new ArgumentNullException(nameof(selectedFilterIds));

            View sourceTemplate = doc.GetElement(sourceTemplateId) as View
                ?? throw new InvalidOperationException("Source template not found.");

            if (!sourceTemplate.IsTemplate)
                throw new InvalidOperationException("Selected source view is not a template.");

            var result = new ApplyViewFiltersResult
            {
                SourceTemplateName = sourceTemplate.Name,
                RequestedTargets = targetTemplateIds.Count,
                RequestedFilters = selectedFilterIds.Count
            };

            if (!copyEnabledState && !copyVisibility && !copyOverrides)
            {
                result.Messages.Add("Nothing selected to copy. Enable at least one filter transfer option.");
                return result;
            }

            if (selectedFilterIds.Count == 0)
            {
                result.Messages.Add("No filters were selected.");
                return result;
            }

            List<ElementId> sourceOrderedFilterIds = sourceTemplate.GetOrderedFilters().ToList();

            List<ElementId> effectiveSelected = sourceOrderedFilterIds
                .Where(id => selectedFilterIds.Any(x => x != null && x.Value == id.Value))
                .ToList();

            if (effectiveSelected.Count == 0)
            {
                result.Messages.Add("None of the selected filters are actually applied to the source template.");
                return result;
            }

            using (Transaction tx = new Transaction(doc, "Apply selected view filters to templates"))
            {
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

                    int appliedCount = 0;

                    try
                    {
                        // 1. Ensure selected filters exist on target
                        EnsureFiltersApplied(targetTemplate, effectiveSelected);

                        // 2. Reorder first if requested
                        if (preserveOrder)
                        {
                            ReorderTargetFiltersLikeSource(targetTemplate, effectiveSelected);
                        }

                        // 3. Apply state after ordering so we do not lose settings
                        foreach (ElementId filterId in effectiveSelected)
                        {
                            ApplySingleFilterState(
                                sourceTemplate,
                                targetTemplate,
                                filterId,
                                copyEnabledState,
                                copyVisibility,
                                copyOverrides);

                            appliedCount++;
                        }

                        if (appliedCount > 0)
                        {
                            result.UpdatedTargets++;
                            result.Messages.Add($"Updated '{targetTemplate.Name}' with {appliedCount} filter(s).");
                        }
                        else
                        {
                            result.SkippedTargets++;
                            result.Messages.Add($"No filters applied to '{targetTemplate.Name}'.");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.SkippedTargets++;
                        result.Messages.Add($"Failed template '{targetTemplate.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            return result;
        }

        private static void EnsureFiltersApplied(View targetTemplate, IList<ElementId> filterIds)
        {
            if (targetTemplate == null) throw new ArgumentNullException(nameof(targetTemplate));
            if (filterIds == null) throw new ArgumentNullException(nameof(filterIds));

            foreach (ElementId filterId in filterIds)
            {
                if (filterId == null || filterId == ElementId.InvalidElementId)
                    continue;

                if (!targetTemplate.IsFilterApplied(filterId))
                {
                    targetTemplate.AddFilter(filterId);
                }
            }
        }

        private static void ApplySingleFilterState(
            View sourceTemplate,
            View targetTemplate,
            ElementId filterId,
            bool copyEnabledState,
            bool copyVisibility,
            bool copyOverrides)
        {
            if (sourceTemplate == null) throw new ArgumentNullException(nameof(sourceTemplate));
            if (targetTemplate == null) throw new ArgumentNullException(nameof(targetTemplate));
            if (filterId == null || filterId == ElementId.InvalidElementId)
                throw new ArgumentException("Invalid filter id.", nameof(filterId));

            if (!sourceTemplate.IsFilterApplied(filterId))
                throw new InvalidOperationException("Filter is not applied to the source template.");

            if (!targetTemplate.IsFilterApplied(filterId))
            {
                targetTemplate.AddFilter(filterId);
            }

            if (copyEnabledState)
            {
                bool isEnabled = sourceTemplate.GetIsFilterEnabled(filterId);
                targetTemplate.SetIsFilterEnabled(filterId, isEnabled);
            }

            if (copyVisibility)
            {
                bool isVisible = sourceTemplate.GetFilterVisibility(filterId);
                targetTemplate.SetFilterVisibility(filterId, isVisible);
            }

            if (copyOverrides)
            {
                OverrideGraphicSettings sourceOgs = sourceTemplate.GetFilterOverrides(filterId);
                OverrideGraphicSettings cloned = CloneOverrideGraphicSettings(sourceOgs);
                targetTemplate.SetFilterOverrides(filterId, cloned);
            }
        }

        private static void ReorderTargetFiltersLikeSource(
            View targetTemplate,
            IList<ElementId> selectedFilterIdsInSourceOrder)
        {
            if (targetTemplate == null) throw new ArgumentNullException(nameof(targetTemplate));
            if (selectedFilterIdsInSourceOrder == null) throw new ArgumentNullException(nameof(selectedFilterIdsInSourceOrder));

            List<ElementId> currentlyApplied = targetTemplate.GetOrderedFilters().ToList();

            List<ElementId> selectedApplied = currentlyApplied
                .Where(id => selectedFilterIdsInSourceOrder.Any(x => x.Value == id.Value))
                .ToList();

            if (selectedApplied.Count == 0)
                return;

            foreach (ElementId id in selectedApplied)
            {
                targetTemplate.RemoveFilter(id);
            }

            foreach (ElementId id in selectedFilterIdsInSourceOrder)
            {
                targetTemplate.AddFilter(id);
            }
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

            // Keep only if available in your referenced Revit API build.
            // clone.SetTransparency(source.Transparency);

            // Keep only if available in your referenced Revit API build.
            clone.SetDetailLevel(source.DetailLevel);

            return clone;
        }
    }
}