// FILE: BA_Tools/Warnings/ExternalEvents/AutoResolveJoinsHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Warnings.Models;

namespace BA.Warnings.ExternalEvents
{
    public enum JoinResolveMode { Preview, Commit }

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

    public sealed class AutoResolveJoinsHandler : IExternalEventHandler
    {
        private static ExternalEvent _event;

        private JoinResolveMode _mode;
        private List<WarningItem> _targets;
        private List<JoinFailureResolutionRule> _rules;
        private List<JoinResolutionPreviewItem> _commitItems;
        private Action<List<JoinResolutionPreviewItem>> _onPreviewCompleted;
        private Action<AutoResolveJoinsResult> _onCommitCompleted;

        public static AutoResolveJoinsHandler Instance { get; } = new AutoResolveJoinsHandler();

        private AutoResolveJoinsHandler() { }

        public void RequestPreview(List<WarningItem> targets, List<JoinFailureResolutionRule> rules, Action<List<JoinResolutionPreviewItem>> onCompleted)
        {
            _mode = JoinResolveMode.Preview;
            _targets = targets;
            _rules = rules;
            _onPreviewCompleted = onCompleted;
            _event ??= ExternalEvent.Create(this);
            _event.Raise();
        }

        public void RequestCommit(List<JoinResolutionPreviewItem> approvedItems, Action<AutoResolveJoinsResult> onCompleted)
        {
            _mode = JoinResolveMode.Commit;
            _commitItems = approvedItems.Where(i => i.Include).ToList();
            _onCommitCompleted = onCompleted;
            _event ??= ExternalEvent.Create(this);
            _event.Raise();
        }

        public void Execute(UIApplication app)
        {
            if (_mode == JoinResolveMode.Preview)
                ExecutePreview(app);
            else
                ExecuteCommit(app);
        }

        private void ExecutePreview(UIApplication app)
        {
            var preview = new List<JoinResolutionPreviewItem>();
            try
            {
                UIDocument uiDoc = app.ActiveUIDocument;
                if (uiDoc == null) return;

                Document doc = uiDoc.Document;

                Dictionary<Guid, JoinFailureResolutionRule> ruleMap = _rules
                    .Where(r => r.Action != JoinResolutionAction.Ignore)
                    .ToDictionary(r => r.FailureDefinitionGuid, r => r);

                foreach (WarningItem w in _targets)
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
            }
            catch (Exception ex)
            {
                AppLogger.LogError("AutoResolveJoinsHandler.ExecutePreview", ex);
            }
            finally
            {
                _onPreviewCompleted?.Invoke(preview);
                _onPreviewCompleted = null;
                _targets = null;
                _rules = null;
            }
        }

        private void ExecuteCommit(UIApplication app)
        {
            var result = new AutoResolveJoinsResult();
            try
            {
                UIDocument uiDoc = app.ActiveUIDocument;
                if (uiDoc == null || _commitItems == null || _commitItems.Count == 0)
                    return;

                Document doc = uiDoc.Document;

                using (var group = new TransactionGroup(doc, "BA Auto-Resolve Joins"))
                {
                    group.Start();

                    foreach (JoinResolutionPreviewItem item in _commitItems)
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
            }
            catch (Exception ex)
            {
                AppLogger.LogError("AutoResolveJoinsHandler.ExecuteCommit", ex);
            }
            finally
            {
                _onCommitCompleted?.Invoke(result);
                _onCommitCompleted = null;
                _commitItems = null;
            }
        }

        public string GetName() => "BA Auto-Resolve Joins";
    }
}