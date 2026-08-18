using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

using BA.BIM.Core.Annotations;

namespace BA.BIM.Commands.Anno
{
    [Transaction(TransactionMode.Manual)]
    public class TagAllSelectedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData c, ref string message, ElementSet elements)
        {
            UIApplication uiapp = c.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            try
            {
                ICollection<ElementId> preSelectedIds = uidoc.Selection.GetElementIds();

                if (preSelectedIds == null || preSelectedIds.Count == 0)
                {
                    TaskDialog.Show("BA", "Select elements to tag first, then run this command.");
                    return Result.Cancelled;
                }

                var taggableFilter = new TaggableElementSelectionFilter(view.Id);

                List<Element> workingSelection = preSelectedIds
                    .Select(id => doc.GetElement(id))
                    .Where(e => e != null && taggableFilter.AllowElement(e))
                    .Distinct(new ElementIdComparer())
                    .ToList();

                if (workingSelection.Count == 0)
                {
                    TaskDialog.Show("BA", "Nothing in the current selection can be tagged. " +
                        "View-specific and pinned elements are excluded. Select model elements first, then run this command.");
                    return Result.Cancelled;
                }

                Dictionary<long, CategoryTagOptions> categoryTagOptions = null;
                TagAllSettingsResult dlgResult = null;

                while (true)
                {
                    categoryTagOptions = TagPlacementEngine.BuildCategoryTagOptions(doc, view, workingSelection);

                    if (categoryTagOptions.Count == 0)
                    {
                        TaskDialog.Show("BA", "None of the selected categories have an available tag type/family loaded in the project.");
                        return Result.Cancelled;
                    }

                    var dlgOutcome = TagAllSelectedDialog.GetResult(categoryTagOptions.Values.ToList());

                    if (dlgOutcome.Action == TagAllDialogAction.Cancel)
                        return Result.Cancelled;

                    if (dlgOutcome.Action == TagAllDialogAction.Proceed)
                    {
                        dlgResult = dlgOutcome.Settings;
                        break;
                    }

                    bool entireProject = dlgOutcome.Action == TagAllDialogAction.ExpandEntireProject;
                    var expanded = ExpandSelectionToAllInstances(doc, view, workingSelection, entireProject);

                    if (expanded.Count == 0)
                    {
                        TaskDialog.Show("BA", "No matching instances found.");
                        continue;
                    }

                    workingSelection = expanded;
                }

                if (dlgResult == null || dlgResult.SelectedTagTypeIdByCategoryKey.Count == 0)
                {
                    TaskDialog.Show("BA", "No categories selected to tag.");
                    return Result.Cancelled;
                }

                var alreadyTaggedIds = TagPlacementEngine.GetAlreadyTaggedElementIds(doc, view);

                var report = new TagPlacementReport { Total = workingSelection.Count };
                var workList = new List<(Element Element, ElementId TagTypeId)>();

                foreach (var kvp in categoryTagOptions)
                {
                    long catKey = kvp.Key;
                    var opts = kvp.Value;

                    if (!dlgResult.SelectedTagTypeIdByCategoryKey.TryGetValue(catKey, out ElementId tagTypeId) || tagTypeId == null)
                        continue;

                    foreach (var e in opts.Elements)
                    {
                        if (alreadyTaggedIds.Contains(e.Id.Value))
                        {
                            report.SkippedAlreadyTagged++;
                            continue;
                        }

                        workList.Add((e, tagTypeId));
                    }
                }

                if (workList.Count == 0)
                {
                    TaskDialog.Show("BA - Tag All",
                        $"Nothing to tag.\nAlready tagged: {report.SkippedAlreadyTagged}");
                    return Result.Cancelled;
                }

                ViewPlane2D plane = ViewPlane2D.FromView(view);

                using (Transaction t = new Transaction(doc, "Tag All Selected"))
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
                        if (newTagBb == null)
                            continue;

                        var newTagRect = AnnoGeometry.GetRectInViewPlane(plane, newTagBb);
                        var newTagRectInflated = newTagRect.Inflate(dlgResult.Gap);

                        var colliding = new List<AnnoItem>();
                        foreach (var ob in obstacles)
                        {
                            if (ob.Element.Id == newTag.Id)
                                continue;

                            var obBb = ob.Element.get_BoundingBox(view);
                            if (obBb == null)
                                continue;

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

                TaskDialog.Show("BA - Tag All report",
                    $"Selected: {report.Total}\n" +
                    $"Created: {report.Created}\n" +
                    $"Already tagged (skipped): {report.SkippedAlreadyTagged}\n" +
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

        private static List<Element> ExpandSelectionToAllInstances(Document doc, View view, List<Element> seedElements, bool entireProject)
        {
            var typeIds = new HashSet<long>(
                seedElements
                    .Select(e => e.GetTypeId())
                    .Where(id => id != ElementId.InvalidElementId)
                    .Select(id => id.Value));

            if (typeIds.Count == 0)
                return seedElements;

            var builtInCats = seedElements
                .Where(e => e.Category != null)
                .Select(e => (BuiltInCategory)(int)e.Category.Id.Value)
                .Distinct()
                .ToList();

            if (builtInCats.Count == 0)
                return seedElements;

            var catFilter = new ElementMulticategoryFilter(builtInCats);

            FilteredElementCollector collector = entireProject
                ? new FilteredElementCollector(doc)
                : new FilteredElementCollector(doc, view.Id);

            var matches = collector
                .WhereElementIsNotElementType()
                .WherePasses(catFilter)
                .Where(e => typeIds.Contains(e.GetTypeId().Value))
                .ToList();

            return matches
                .Distinct(new ElementIdComparer())
                .ToList();
        }
    }

    internal class TaggableElementSelectionFilter : ISelectionFilter
    {
        private readonly ElementId _viewId;
        public TaggableElementSelectionFilter(ElementId viewId) => _viewId = viewId;

        public bool AllowElement(Element elem)
        {
            if (elem == null) return false;
            if (elem.Category == null) return false;
            if (elem.ViewSpecific) return false;
            if (elem.Pinned) return false;
            return true;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}