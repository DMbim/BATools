using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

using BA.BIM.Core.Annotations;
using BA.BIM.Core.ViewScoping;

namespace BA.BIM.Commands.Anno
{
    /// <summary>
    /// Multi-view counterpart to TagAllSelectedCommand. No selection prompt - instead
    /// scans every target view (per BA_ViewScopeResolver) for untagged elements in
    /// any category that has a valid tag type (TagPlacementEngine.CategoryToTagCategories),
    /// aggregates them into the SAME dialog TagAllSelectedCommand already uses for
    /// tag-type-per-category selection, then commits per view with the SAME collision
    /// resolution engine.
    ///
    /// Best-effort semantics: one Transaction PER VIEW. A commit failure in one view
    /// does not roll back tags already committed in other views - identical boundary
    /// to the Auto-Dimensioner's placement service, for the same reason (hundreds of
    /// views means a single bad view should not void the whole run).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class TagAllBatchCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData c, ref string message, ElementSet elements)
        {
            UIApplication uiapp = c.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                var scopeResult = TagAllBatchScopeDialog.GetResult();
                if (scopeResult == null)
                    return Result.Cancelled;

                var targetViews = BA_ViewScopeResolver.Resolve(doc, scopeResult.Mode, uidoc.ActiveView, null)
                    .Cast<View>()
                    .ToList();

                if (targetViews.Count == 0)
                {
                    TaskDialog.Show("BA - Tag All (Batch)", "No target views resolved for the selected scope.");
                    return Result.Cancelled;
                }

                // ---------------- 1. Scan every view (read-only) for candidate elements ----------------

                var perViewCandidates = new Dictionary<ElementId, List<Element>>();
                var allElementsForDialog = new List<Element>();

                foreach (var view in targetViews)
                {
                    HashSet<long> alreadyTagged;
                    try
                    {
                        alreadyTagged = TagPlacementEngine.GetAlreadyTaggedElementIds(doc, view);
                    }
                    catch
                    {
                        continue; // view can't be queried (e.g. closed/invalid state) - skip, don't abort the whole scan
                    }

                    var viewElements = new List<Element>();

                    foreach (var hostCat in TagPlacementEngine.CategoryToTagCategories.Keys)
                    {
                        List<Element> collected;
                        try
                        {
                            collected = new FilteredElementCollector(doc, view.Id)
                                .OfCategory(hostCat)
                                .WhereElementIsNotElementType()
                                .Where(e => e.Category != null && !alreadyTagged.Contains(e.Id.Value))
                                .ToList();
                        }
                        catch
                        {
                            continue; // category not valid/collectible in this view type - skip category for this view
                        }

                        if (collected.Count == 0) continue;

                        viewElements.AddRange(collected);
                        allElementsForDialog.AddRange(collected);
                    }

                    if (viewElements.Count > 0)
                        perViewCandidates[view.Id] = viewElements;
                }

                if (perViewCandidates.Count == 0)
                {
                    TaskDialog.Show("BA - Tag All (Batch)", "Nothing to tag - no untagged, taggable elements found across the target views.");
                    return Result.Cancelled;
                }

                // ---------------- 2. Category / tag type / settings dialog (reused unchanged) ----------------

                // View passed here is only used for Document.GetDefaultFamilyTypeId lookups,
                // which are document-level, not view-level - any target view works.
                var categoryTagOptions = TagPlacementEngine.BuildCategoryTagOptions(doc, targetViews[0], allElementsForDialog);

                if (categoryTagOptions.Count == 0)
                {
                    TaskDialog.Show("BA - Tag All (Batch)", "None of the found categories have an available tag type/family loaded in the project.");
                    return Result.Cancelled;
                }

                var dlgOutcome = TagAllSelectedDialog.GetResult(categoryTagOptions.Values.ToList());

                if (dlgOutcome.Action != TagAllDialogAction.Proceed)
                    return Result.Cancelled; // batch mode has no "expand selection" concept - Expand buttons are inert here, Proceed/Cancel only apply

                var dlgResult = dlgOutcome.Settings;

                if (dlgResult == null || dlgResult.SelectedTagTypeIdByCategoryKey.Count == 0)
                {
                    TaskDialog.Show("BA - Tag All (Batch)", "No categories selected to tag.");
                    return Result.Cancelled;
                }

                // ---------------- 3. Commit, one Transaction per view, best effort ----------------

                var report = new TagPlacementReport { Total = allElementsForDialog.Count };

                foreach (var kvp in perViewCandidates)
                {
                    var view = doc.GetElement(kvp.Key) as View;
                    var candidateElements = kvp.Value;

                    if (view == null)
                    {
                        report.ViewsFailed++;
                        continue;
                    }

                    try
                    {
                        CommitOneView(doc, view, candidateElements, dlgResult, report);
                        report.ViewsProcessed++;
                    }
                    catch (Exception ex)
                    {
                        report.ViewsFailed++;
                        report.FailReasons[$"View '{view.Name}': {ex.GetType().Name}"] =
                            report.FailReasons.GetValueOrDefault($"View '{view.Name}': {ex.GetType().Name}") + 1;
                    }
                }

                TaskDialog.Show("BA - Tag All (Batch) report",
                    $"Views processed: {report.ViewsProcessed}\n" +
                    $"Views failed: {report.ViewsFailed}\n" +
                    $"Candidate elements: {report.Total}\n" +
                    $"Created: {report.Created}\n" +
                    $"Skipped (already tagged at commit time): {report.SkippedAlreadyTagged}\n" +
                    $"Collided and resolved: {report.CollidedAndResolved}\n" +
                    $"Failed to create: {report.FailedToCreate}\n" +
                    $"Final recheck, elements moved: {report.FinalPassMoved}\n" +
                    $"Final recheck, remaining collisions: {report.FinalPassRemainingCollisions}");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }

        private static void CommitOneView(
            Document doc, View view, List<Element> candidateElements, TagAllSettingsResult dlgResult, TagPlacementReport report)
        {
            // Re-check already-tagged status HERE, not from the earlier scan - the scan
            // pass and this commit pass are not atomic across a batch of potentially
            // hundreds of views, and the same element/category filters must be re-evaluated
            // against current document state at commit time.
            var alreadyTaggedIds = TagPlacementEngine.GetAlreadyTaggedElementIds(doc, view);

            var workList = new List<(Element Element, ElementId TagTypeId)>();
            foreach (var e in candidateElements)
            {
                if (e.Category == null) continue;
                if (alreadyTaggedIds.Contains(e.Id.Value))
                {
                    report.SkippedAlreadyTagged++;
                    continue;
                }

                if (!dlgResult.SelectedTagTypeIdByCategoryKey.TryGetValue(e.Category.Id.Value, out ElementId tagTypeId) || tagTypeId == null)
                    continue; // category was unchecked in the dialog

                workList.Add((e, tagTypeId));
            }

            if (workList.Count == 0)
                return;

            ViewPlane2D plane = ViewPlane2D.FromView(view);

            using (Transaction t = new Transaction(doc, $"BA Tag All (Batch) - {view.Name}"))
            {
                t.Start();

                var preexistingObstacles = TagPlacementEngine.CollectObstacleItems(doc, view);
                var obstacles = new List<AnnoItem>(preexistingObstacles);
                var newTagItems = new List<AnnoItem>();

                foreach (var (element, tagTypeId) in workList)
                {
                    IndependentTag newTag;

                    try
                    {
                        newTag = TagPlacementEngine.CreateTag(doc, view, element, tagTypeId, dlgResult.UseLeader);
                    }
                    catch (Exception ex)
                    {
                        report.FailedToCreate++;
                        report.FailReasons[ex.GetType().Name] = report.FailReasons.GetValueOrDefault(ex.GetType().Name) + 1;
                        continue;
                    }

                    if (newTag == null)
                    {
                        report.FailedToCreate++;
                        continue;
                    }

                    report.Created++;

                    var newTagBb = newTag.get_BoundingBox(view);
                    if (newTagBb == null) continue;

                    var newTagRect = AnnoGeometry.GetRectInViewPlane(plane, newTagBb);
                    var newTagRectInflated = newTagRect.Inflate(dlgResult.Gap);

                    var colliding = new List<AnnoItem>();
                    foreach (var ob in obstacles)
                    {
                        if (ob.Element.Id == newTag.Id) continue;

                        var obBb = ob.Element.get_BoundingBox(view);
                        if (obBb == null) continue;

                        var obRect = AnnoGeometry.GetRectInViewPlane(plane, obBb).Inflate(dlgResult.Gap);
                        if (newTagRectInflated.IntersectsInclusive(obRect))
                            colliding.Add(ob);
                    }

                    if (colliding.Count > 0)
                    {
                        TagPlacementEngine.ResolveTagCollision(doc, view, plane, newTag, newTagRectInflated, colliding, dlgResult);
                        report.CollidedAndResolved++;
                    }

                    var refreshedBb = newTag.get_BoundingBox(view);
                    if (refreshedBb != null)
                    {
                        var newTagAnnoItem = new AnnoItem(newTag, refreshedBb);
                        obstacles.Add(newTagAnnoItem);
                        newTagItems.Add(newTagAnnoItem);
                    }
                }

                if (newTagItems.Count > 0)
                {
                    TagPlacementEngine.RunFinalGlobalRecheck(doc, view, plane, newTagItems, preexistingObstacles, dlgResult, report);
                }

                t.Commit();
            }
        }
    }
}