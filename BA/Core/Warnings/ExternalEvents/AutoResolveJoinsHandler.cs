// FILE: BA_Tools/Warnings/ExternalEvents/AutoResolveJoinsHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.UI.ExternalEvents;
using BA.Warnings.Models;

namespace BA.Warnings.ExternalEvents
{
    public sealed class JoinResolutionPreviewItem
    {
        public WarningItem SourceWarning { get; set; }
        public ElementId ElementA { get; set; }
        public ElementId ElementB { get; set; }
        public JoinResolutionAction ProposedAction { get; set; }
        public bool CurrentlyJoined { get; set; }
        public bool Include { get; set; } = true;
        public string Note { get; set; } = string.Empty;
    }

    public sealed class AutoResolveJoinsResult
    {
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public int SkippedStale { get; set; }
    }

    public static class AutoResolveJoinsHandler
    {
        public static void RequestPreview(List<WarningItem> targets, List<JoinFailureResolutionRule> rules,
            Action<List<JoinResolutionPreviewItem>> onCompleted)
        {
            AppExternalInvoker.Instance.Run(
                app => ExecutePreview(app, targets, rules),
                onCompleted,
                ex =>
                {
                    AppLogger.LogError("AutoResolveJoinsHandler.RequestPreview", ex);
                    onCompleted?.Invoke(new List<JoinResolutionPreviewItem>());
                });
        }

        public static void RequestCommit(List<JoinResolutionPreviewItem> approvedItems, Action<AutoResolveJoinsResult> onCompleted)
        {
            List<JoinResolutionPreviewItem> toCommit = approvedItems.Where(i => i.Include).ToList();

            AppExternalInvoker.Instance.Run(
                app => ExecuteCommit(app, toCommit),
                onCompleted,
                ex =>
                {
                    AppLogger.LogError("AutoResolveJoinsHandler.RequestCommit", ex);
                    onCompleted?.Invoke(new AutoResolveJoinsResult());
                });
        }

        private static List<JoinResolutionPreviewItem> ExecutePreview(UIApplication app, List<WarningItem> targets, List<JoinFailureResolutionRule> rules)
        {
            var preview = new List<JoinResolutionPreviewItem>();

            UIDocument uiDoc = app.ActiveUIDocument;
            if (uiDoc == null) return preview;
            Document doc = uiDoc.Document;

            Dictionary<Guid, JoinFailureResolutionRule> ruleMap = rules
                .Where(r => r.Action != JoinResolutionAction.Ignore)
                .ToDictionary(r => r.FailureDefinitionGuid, r => r);

            foreach (WarningItem w in targets)
            {
                if (!ruleMap.TryGetValue(w.FailureDefinitionId.Guid, out JoinFailureResolutionRule rule))
                    continue;

                List<ElementId> ids = w.FailingElementIds.Where(id => doc.GetElement(id) != null).ToList();

                if (ids.Count < 2)
                {
                    preview.Add(new JoinResolutionPreviewItem
                    {
                        SourceWarning = w,
                        ProposedAction = rule.Action,
                        Include = false,
                        Note = "Fewer than two live elements in this warning, cannot resolve automatically."
                    });
                    continue;
                }

                for (int i = 0; i < ids.Count - 1; i++)
                {
                    for (int j = i + 1; j < ids.Count; j++)
                    {
                        Element elA = doc.GetElement(ids[i]);
                        Element elB = doc.GetElement(ids[j]);
                        bool joined;

                        try
                        {
                            joined = JoinGeometryUtils.AreElementsJoined(doc, elA, elB);
                        }
                        catch (Exception ex)
                        {
                            preview.Add(new JoinResolutionPreviewItem
                            {
                                SourceWarning = w,
                                ElementA = ids[i],
                                ElementB = ids[j],
                                ProposedAction = rule.Action,
                                Include = false,
                                Note = $"AreElementsJoined threw: {ex.Message}"
                            });
                            continue;
                        }

                        bool needsAction = (rule.Action == JoinResolutionAction.Join && !joined)
                                         || (rule.Action == JoinResolutionAction.Unjoin && joined);

                        preview.Add(new JoinResolutionPreviewItem
                        {
                            SourceWarning = w,
                            ElementA = ids[i],
                            ElementB = ids[j],
                            ProposedAction = rule.Action,
                            CurrentlyJoined = joined,
                            Include = needsAction,
                            Note = needsAction ? string.Empty : "Already in the target join state, nothing to do."
                        });
                    }
                }
            }

            return preview;
        }

        private static AutoResolveJoinsResult ExecuteCommit(UIApplication app, List<JoinResolutionPreviewItem> commitItems)
        {
            var result = new AutoResolveJoinsResult();

            UIDocument uiDoc = app.ActiveUIDocument;
            if (uiDoc == null || commitItems.Count == 0) return result;
            Document doc = uiDoc.Document;

            using (var group = new TransactionGroup(doc, "BA Auto-Resolve Joins"))
            {
                group.Start();

                foreach (JoinResolutionPreviewItem item in commitItems)
                {
                    Element elA = doc.GetElement(item.ElementA);
                    Element elB = doc.GetElement(item.ElementB);

                    if (elA == null || elB == null)
                    {
                        result.SkippedStale++;
                        continue;
                    }

                    using (var t = new Transaction(doc, item.ProposedAction == JoinResolutionAction.Join
                                                              ? "BA Join Elements"
                                                              : "BA Unjoin Elements"))
                    {
                        t.Start();
                        try
                        {
                            if (item.ProposedAction == JoinResolutionAction.Join)
                                JoinGeometryUtils.JoinGeometry(doc, elA, elB);
                            else
                                JoinGeometryUtils.UnjoinGeometry(doc, elA, elB);

                            t.Commit();
                            result.Succeeded++;
                        }
                        catch (Exception ex)
                        {
                            t.RollBack();
                            result.Failed++;
                            AppLogger.LogError($"AutoResolveJoinsHandler.ExecuteCommit ({item.ElementA.Value}<->{item.ElementB.Value})", ex);
                        }
                    }
                }

                group.Assimilate();
            }

            return result;
        }
    }
}