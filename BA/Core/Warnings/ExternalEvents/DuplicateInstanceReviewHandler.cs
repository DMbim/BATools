// FILE: BA_Tools/Warnings/ExternalEvents/DuplicateInstanceReviewHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.UI.ExternalEvents;
using BA.Warnings.Helpers;
using BA.Warnings.Models;

namespace BA.Warnings.ExternalEvents
{
    public sealed class DuplicateDeleteResult
    {
        public int GroupsProcessed { get; set; }
        public int ElementsDeleted { get; set; }
        public int GroupsSkippedWouldEmptyGroup { get; set; }
        public int GroupsSkippedStale { get; set; }
        public int GroupsFailed { get; set; }
    }

    public static class DuplicateInstanceReviewHandler
    {
        private static readonly HashSet<Guid> EligibleDuplicateGuids = new HashSet<Guid>
        {
            new Guid("05e62a0c-c174-4a63-ab54-9991a3819df9") // "duplicate instances of the same family in the same place"
        };

        public static bool IsEligible(Guid failureDefinitionGuid) => EligibleDuplicateGuids.Contains(failureDefinitionGuid);

        public static void RequestPreview(List<WarningItem> allWarnings, Action<List<DuplicateInstanceGroup>> onCompleted)
        {
            AppExternalInvoker.Instance.Run(
                app => ExecutePreview(app, allWarnings),
                onCompleted,
                ex =>
                {
                    AppLogger.LogError("DuplicateInstanceReviewHandler.RequestPreview", ex);
                    onCompleted?.Invoke(new List<DuplicateInstanceGroup>());
                });
        }

        public static void RequestCommit(List<DuplicateInstanceGroup> groups, Action<DuplicateDeleteResult> onCompleted)
        {
            AppExternalInvoker.Instance.Run(
                app => ExecuteCommit(app, groups),
                onCompleted,
                ex =>
                {
                    AppLogger.LogError("DuplicateInstanceReviewHandler.RequestCommit", ex);
                    onCompleted?.Invoke(new DuplicateDeleteResult());
                });
        }

        private static List<DuplicateInstanceGroup> ExecutePreview(UIApplication app, List<WarningItem> targets)
        {
            var groups = new List<DuplicateInstanceGroup>();

            UIDocument uiDoc = app.ActiveUIDocument;
            if (uiDoc == null) return groups;
            Document doc = uiDoc.Document;

            foreach (WarningItem w in targets)
            {
                if (!IsEligible(w.FailureDefinitionId.Guid)) continue;

                List<ElementId> liveIds = w.AllElementIds.Where(id => doc.GetElement(id) != null).ToList();
                if (liveIds.Count < 2) continue;

                long minIdValue = liveIds.Min(id => id.Value);

                var group = new DuplicateInstanceGroup { SourceWarning = w };

                foreach (ElementId id in liveIds)
                {
                    Element el = doc.GetElement(id);
                    group.Candidates.Add(new DuplicateInstanceCandidate
                    {
                        ElementId = id,
                        Category = el.Category?.Name ?? "(no category)",
                        FamilyAndType = ElementDisplayHelper.GetFamilyAndTypeName(doc, el),
                        IsLikelyOlder = id.Value == minIdValue,
                        FilledParameterCount = ElementDisplayHelper.CountFilledParameters(el),
                        ParameterSnapshot = ElementDisplayHelper.GetParameterSnapshot(el)
                    });
                }

                groups.Add(group);
            }

            return groups;
        }

        private static DuplicateDeleteResult ExecuteCommit(UIApplication app, List<DuplicateInstanceGroup> commitGroups)
        {
            var result = new DuplicateDeleteResult();

            UIDocument uiDoc = app.ActiveUIDocument;
            if (uiDoc == null || commitGroups == null) return result;
            Document doc = uiDoc.Document;

            using (var group = new TransactionGroup(doc, "BA Delete Duplicate Instances"))
            {
                group.Start();

                foreach (DuplicateInstanceGroup g in commitGroups)
                {
                    result.GroupsProcessed++;

                    List<ElementId> liveCandidateIds = g.Candidates
                        .Where(c => doc.GetElement(c.ElementId) != null)
                        .Select(c => c.ElementId)
                        .ToList();

                    if (liveCandidateIds.Count == 0)
                    {
                        result.GroupsSkippedStale++;
                        continue;
                    }

                    List<ElementId> toDelete = g.Candidates
                        .Where(c => c.MarkedForDeletion && doc.GetElement(c.ElementId) != null)
                        .Select(c => c.ElementId)
                        .ToList();

                    if (toDelete.Count == 0) continue;

                    if (toDelete.Count >= liveCandidateIds.Count)
                    {
                        result.GroupsSkippedWouldEmptyGroup++;
                        continue;
                    }

                    using (var t = new Transaction(doc, "BA Delete Duplicate Instance"))
                    {
                        t.Start();
                        try
                        {
                            doc.Delete(toDelete);
                            t.Commit();
                            result.ElementsDeleted += toDelete.Count;
                        }
                        catch (Exception ex)
                        {
                            t.RollBack();
                            result.GroupsFailed++;
                            AppLogger.LogError($"DuplicateInstanceReviewHandler.ExecuteCommit (source: {g.SourceWarning?.Description})", ex);
                        }
                    }
                }

                group.Assimilate();
            }

            return result;
        }
    }
}