// File: BA_Tools/CadPurge/Services/LinePatternMappingService.cs
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BA.CadPurge.Models;

namespace BA.CadPurge.Services
{
    /// <summary>
    /// Applies Delete and MapToStandard actions to LinePattern PurgeCandidates.
    ///
    /// Mapping a line pattern means redirecting every Line Style subcategory whose Projection or
    /// Cut line pattern currently points at the source LinePatternElement to point at the resolved
    /// target instead, then deleting the now-orphaned source. Line patterns are referenced by
    /// Category/GraphicsStyle subcategories (Document.Settings.Categories), a small, cheaply
    /// enumerable set, not by individual line elements directly.
    ///
    /// Known limitation: view-specific Visibility/Graphics overrides
    /// (OverrideGraphicSettings.SetProjectionLinePatternId) can also reference a LinePatternElement
    /// directly, per view, per category or per element. Enumerating every view's overrides across
    /// the whole document to find those would be a genuinely expensive full scan, and this class
    /// does not attempt it. If a source pattern is deleted while still referenced only by a stray
    /// view override, Revit raises a warning during the transaction rather than corrupting
    /// anything; PurgeBatchExecutor's CadPurgeFailuresPreprocessor auto-dismisses that warning and
    /// records its text on the candidate's StatusDetail.
    ///
    /// Every public method here requires an already-open Transaction/SubTransaction on doc
    /// (doc.IsModifiable). This class never opens its own transaction, so PurgeBatchExecutor can
    /// wrap each candidate in its own SubTransaction for best-effort, per-candidate isolation.
    /// </summary>
    public sealed class LinePatternMappingService
    {
        private static readonly GraphicsStyleType[] LinePatternStyleTypes =
        {
            GraphicsStyleType.Projection,
            GraphicsStyleType.Cut
        };

        /// <summary>
        /// Redirects every subcategory pointing at candidate.ElementId to
        /// candidate.ResolvedTargetElementId, then deletes candidate.ElementId. Throws if
        /// candidate.TargetSource indicates no valid target was resolved.
        /// </summary>
        public void ApplyMapping(Document doc, PurgeCandidate candidate)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (candidate.ItemType != PurgeItemType.LinePattern)
                throw new ArgumentException($"LinePatternMappingService cannot process ItemType '{candidate.ItemType}'.", nameof(candidate));
            if (!doc.IsModifiable)
                throw new InvalidOperationException("ApplyMapping requires an active Transaction on doc.");
            if (candidate.TargetSource != MappingTargetSource.AlreadyInProject && candidate.TargetSource != MappingTargetSource.LoadedFromTemplate)
                throw new InvalidOperationException($"Cannot map '{candidate.Name}'. No resolved target (TargetSource = {candidate.TargetSource}). CorporateStandardResolverService.ResolveTarget must succeed before calling ApplyMapping.");
            if (candidate.ResolvedTargetElementId == null || candidate.ResolvedTargetElementId == ElementId.InvalidElementId)
                throw new InvalidOperationException($"Cannot map '{candidate.Name}'. ResolvedTargetElementId is invalid.");

            int redirectedCount = RedirectCategoryLinePatternReferences(doc, candidate.ElementId, candidate.ResolvedTargetElementId);

            ICollection<ElementId> deletedIds = doc.Delete(candidate.ElementId);

            candidate.Status = PurgeCandidateStatus.ActionApplied;
            candidate.StatusDetail = redirectedCount > 0
                ? $"Redirected {redirectedCount} line style reference(s) to '{candidate.ResolvedTargetName}'. Deleted '{candidate.Name}' ({(deletedIds?.Count ?? 0)} element(s) removed in total)."
                : $"No line style subcategories referenced '{candidate.Name}' directly. It may only have been used via a view-specific graphic override, which this tool does not enumerate. Deleted '{candidate.Name}' ({(deletedIds?.Count ?? 0)} element(s) removed in total).";
        }

        /// <summary>
        /// Deletes candidate.ElementId directly with no redirection. Any subcategory still
        /// pointing at it when deleted is Revit's own problem to resolve, typically reverting to
        /// no override. This is the deliberately blunt action a BIM manager chooses when they want
        /// the pattern gone with no corporate-standard replacement, as opposed to ApplyMapping's
        /// safer redirect.
        /// </summary>
        public void ApplyDeletion(Document doc, PurgeCandidate candidate)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (candidate.ItemType != PurgeItemType.LinePattern)
                throw new ArgumentException($"LinePatternMappingService cannot process ItemType '{candidate.ItemType}'.", nameof(candidate));
            if (!doc.IsModifiable)
                throw new InvalidOperationException("ApplyDeletion requires an active Transaction on doc.");

            ICollection<ElementId> deletedIds = doc.Delete(candidate.ElementId);

            candidate.Status = PurgeCandidateStatus.ActionApplied;
            candidate.StatusDetail = $"Deleted '{candidate.Name}' with no replacement mapping ({(deletedIds?.Count ?? 0)} element(s) removed in total).";
        }

        private static int RedirectCategoryLinePatternReferences(Document doc, ElementId sourceId, ElementId targetId)
        {
            int redirectedCount = 0;
            var visited = new HashSet<long>();

            foreach (Category category in EnumerateAllCategories(doc, visited))
            {
                foreach (GraphicsStyleType styleType in LinePatternStyleTypes)
                {
                    ElementId current;
                    try
                    {
                        current = category.GetLinePatternId(styleType);
                    }
                    catch (Exception)
                    {
                        // Not every category supports every GraphicsStyleType (for example
                        // non-cuttable categories). Skip rather than fail the whole redirect
                        // pass over one category.
                        continue;
                    }

                    if (current != null && current == sourceId)
                    {
                        category.SetLinePatternId(targetId, styleType);
                        redirectedCount++;
                    }
                }
            }

            return redirectedCount;
        }

        private static IEnumerable<Category> EnumerateAllCategories(Document doc, HashSet<long> visited)
        {
            Categories categories = doc.Settings.Categories;

            foreach (Category topCategory in categories)
            {
                foreach (Category category in EnumerateCategoryAndSubcategories(topCategory, visited))
                    yield return category;
            }
        }

        private static IEnumerable<Category> EnumerateCategoryAndSubcategories(Category category, HashSet<long> visited)
        {
            if (category == null) yield break;
            if (!visited.Add(category.Id.Value)) yield break;

            yield return category;

            if (category.SubCategories == null) yield break;

            foreach (Category sub in category.SubCategories)
            {
                foreach (Category nested in EnumerateCategoryAndSubcategories(sub, visited))
                    yield return nested;
            }
        }
    }
}