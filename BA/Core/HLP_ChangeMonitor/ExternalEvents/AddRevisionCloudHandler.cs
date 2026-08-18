using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core;

namespace BA.UI
{
    public class AddRevisionCloudHandler : IExternalEventHandler
    {
        private UIApplication _uiApp;

        private readonly List<(ElementId viewId, ElementId elementId)> _targets =
            new List<(ElementId viewId, ElementId elementId)>();

        public void Request(UIApplication uiApp, IEnumerable<ChangeRecordRow> rows)
        {
            _uiApp = uiApp;
            _targets.Clear();

            if (rows == null) return;

            foreach (var row in rows)
            {
                if (row == null) continue;

                IEnumerable<ChangeRecord> records;

                if (row.IsGroup && row.GroupRecords != null)
                    records = row.GroupRecords;
                else if (row.GroupRecords != null && row.GroupRecords.Count > 0)
                    records = row.GroupRecords;
                else
                    records = Enumerable.Empty<ChangeRecord>();

                foreach (var r in records)
                {
                    if (r.ElementId == null || r.ElementId == ElementId.InvalidElementId)
                        continue;

                    var vId = (r.ViewId != null && r.ViewId != ElementId.InvalidElementId)
                        ? r.ViewId
                        : ElementId.InvalidElementId;

                    _targets.Add((vId, r.ElementId));
                }
            }
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = _uiApp?.ActiveUIDocument;
                if (uiDoc == null) return;

                var doc = uiDoc.Document;
                if (doc == null) return;

                if (_targets.Count == 0)
                {
                    TaskDialog.Show("Change Monitor", "No elements selected for revision clouds.");
                    return;
                }

                // Ask user which revision to use / edit / create
                var choice = AskRevisionChoice(doc);
                if (choice.Cancelled)
                    return;

                var activeViewId = uiDoc.ActiveView?.Id;
                int createdCount = 0;

                using (var t = new Transaction(doc, "Add Revision Clouds for Changes"))
                {
                    t.Start();

                    // Resolve / edit / create the Revision first
                    Revision revision = null;

                    if (choice.UseExisting)
                    {
                        revision = doc.GetElement(choice.ExistingRevisionId) as Revision;
                        if (revision == null)
                        {
                            TaskDialog.Show("Change Monitor",
                                "The selected revision no longer exists.");
                            t.RollBack();
                            return;
                        }

                        if (choice.EditExisting)
                        {
                            if (!string.IsNullOrWhiteSpace(choice.Description))
                                revision.Description = choice.Description;

                            if (!string.IsNullOrWhiteSpace(choice.DateText))
                                revision.RevisionDate = choice.DateText;
                        }
                    }
                    else
                    {
                        revision = Revision.Create(doc);
                        if (!string.IsNullOrWhiteSpace(choice.Description))
                            revision.Description = choice.Description;
                        if (!string.IsNullOrWhiteSpace(choice.DateText))
                            revision.RevisionDate = choice.DateText;
                    }

                    ElementId revisionId = revision.Id;

                    // Now create clouds
                    foreach (var group in _targets.GroupBy(x => x.viewId))
                    {
                        ElementId viewId = group.Key;
                        if (viewId == null || viewId == ElementId.InvalidElementId)
                            viewId = activeViewId;

                        var view = doc.GetElement(viewId) as View;
                        if (view == null || view.IsTemplate)
                            view = uiDoc.ActiveView;

                        if (view == null) continue;

                        foreach (var eid in group
                                     .Select(g => g.elementId)
                                     .Distinct())
                        {
                            var el = doc.GetElement(eid);
                            if (el == null) continue;

                            var bbox = el.get_BoundingBox(view) ?? el.get_BoundingBox(null);
                            if (bbox == null) continue;

                            double dx = bbox.Max.X - bbox.Min.X;
                            double dy = bbox.Max.Y - bbox.Min.Y;
                            double span = Math.Max(dx, dy);
                            double margin = Math.Max(span * 0.1, 0.5); // 10% or 0.5ft

                            double z = bbox.Min.Z;

                            XYZ p1 = new XYZ(bbox.Min.X - margin, bbox.Min.Y - margin, z);
                            XYZ p2 = new XYZ(bbox.Max.X + margin, bbox.Min.Y - margin, z);
                            XYZ p3 = new XYZ(bbox.Max.X + margin, bbox.Max.Y + margin, z);
                            XYZ p4 = new XYZ(bbox.Min.X - margin, bbox.Max.Y + margin, z);

                            // CLOCKWISE loop: p1 -> p4 -> p3 -> p2 -> p1
                            IList<Curve> curves = new List<Curve>
                            {
                                Line.CreateBound(p1, p4),
                                Line.CreateBound(p4, p3),
                                Line.CreateBound(p3, p2),
                                Line.CreateBound(p2, p1)
                            };

                            try
                            {
                                RevisionCloud.Create(doc, view, revisionId, curves);
                                createdCount++;
                            }
                            catch
                            {
                                // Skip element if cloud creation fails for this one
                            }
                        }
                    }

                    t.Commit();
                }

                if (createdCount > 0)
                {
                    TaskDialog.Show("Change Monitor",
                        $"Created {createdCount} revision cloud(s) around changed elements.");
                }
                else
                {
                    TaskDialog.Show("Change Monitor",
                        "No revision clouds were created.\n\n" +
                        "Possible reasons:\n" +
                        "- Selected elements have no 2D extents in the target views.\n" +
                        "- The chosen revision cannot be used in those views.\n" +
                        "- Elements are in views that cannot host revision clouds.");
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Change Monitor - Error",
                    "Failed to create revision clouds:\n\n" + ex.Message);
            }
        }

        /// <summary>
        /// Ask user which revision to use, and whether to edit or create.
        /// </summary>
        private static RevisionSelectionResult AskRevisionChoice(Document doc)
        {
            var result = new RevisionSelectionResult();

            var revisions = new FilteredElementCollector(doc)
                .OfClass(typeof(Revision))
                .Cast<Revision>()
                .Where(r => !r.Issued) // only non-issued
                .OrderBy(r => r.SequenceNumber)
                .ToList();

            if (revisions.Count == 0)
            {
                // No revision -> force user to create one
                var wnd = new RevisionDetailsWindow(
                    "Create new revision",
                    initialDescription: "",
                    initialDateText: DateTime.Now.ToShortDateString());

                bool? dlg = wnd.ShowDialog();
                if (dlg != true)
                {
                    result.Cancelled = true;
                    return result;
                }

                result.Cancelled = false;
                result.UseExisting = false; // create new
                result.EditExisting = false;
                result.Description = wnd.Description;
                result.DateText = wnd.DateText;
                return result;
            }

            var latest = revisions.Last();
            string latestInfo =
                $"Revision Number: {latest.RevisionNumber}\n" +
                $"Description:    {latest.Description}\n" +
                $"Date:           {latest.RevisionDate}";

            var td = new TaskDialog("Revision for Revision Clouds")
            {
                MainInstruction = "Choose revision for new clouds",
                MainContent = latestInfo + "\n\nWhat would you like to do?",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Use latest revision as-is");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Use latest revision but edit description / date");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Create a new revision");

            var res = td.Show();

            if (res == TaskDialogResult.Cancel)
            {
                result.Cancelled = true;
                return result;
            }

            if (res == TaskDialogResult.CommandLink1)
            {
                result.Cancelled = false;
                result.UseExisting = true;
                result.EditExisting = false;
                result.ExistingRevisionId = latest.Id;
                return result;
            }

            if (res == TaskDialogResult.CommandLink2)
            {
                var wnd = new RevisionDetailsWindow(
                    "Edit latest revision",
                    initialDescription: latest.Description,
                    initialDateText: latest.RevisionDate);

                bool? dlg = wnd.ShowDialog();
                if (dlg == true)
                {
                    result.Cancelled = false;
                    result.UseExisting = true;
                    result.EditExisting = true;
                    result.ExistingRevisionId = latest.Id;
                    result.Description = wnd.Description;
                    result.DateText = wnd.DateText;
                }
                else
                {
                    result.Cancelled = true;
                }

                return result;
            }

            if (res == TaskDialogResult.CommandLink3)
            {
                var wnd = new RevisionDetailsWindow(
                    "Create new revision",
                    initialDescription: "",
                    initialDateText: DateTime.Now.ToShortDateString());

                bool? dlg = wnd.ShowDialog();
                if (dlg == true)
                {
                    result.Cancelled = false;
                    result.UseExisting = false;
                    result.EditExisting = false;
                    result.Description = wnd.Description;
                    result.DateText = wnd.DateText;
                }
                else
                {
                    result.Cancelled = true;
                }

                return result;
            }

            // Fallback
            result.Cancelled = true;
            return result;
        }

        public string GetName() => "BA Add Revision Cloud Handler";
    }
}
