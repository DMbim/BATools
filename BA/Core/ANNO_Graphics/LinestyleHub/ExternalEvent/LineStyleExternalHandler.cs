using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.UI.LineStyleHub.ExternalEvents
{
    /// <summary>
    /// IExternalEventHandler that applies all pending line style edits in a single transaction.
    ///
    /// Operation order per row:
    ///   1. Validate usage if marked for delete (block if in use)
    ///   2. Apply color, weight, pattern changes on the Category
    ///   3. Apply rename (must come after graphic changes — rename can invalidate category ref in some edge cases)
    ///   4. Delete (last, after all other edits on that row are skipped)
    /// </summary>
    internal sealed class LineStyleExternalHandler : IExternalEventHandler
    {
        private readonly object _lock = new();
        private ApplyLineStyleEditsRequest? _request;

        public void SetRequest(ApplyLineStyleEditsRequest req)
        {
            lock (_lock) _request = req;
        }

        public void Execute(UIApplication app)
        {
            ApplyLineStyleEditsRequest? req;
            lock (_lock)
            {
                req = _request;
                _request = null;
            }

            if (req == null) return;

            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                req.Done("No active document.", Array.Empty<string>());
                return;
            }

            var rows = req.Rows.ToList();
            var errors = new List<string>();
            var blockedDeletes = new List<string>();
            int okCount = 0;
            int deletedCount = 0;

            // Pre-validate all deletions before opening the transaction.
            // This avoids partial failure inside the transaction.
            var deleteRows = rows.Where(r => r.IsMarkedForDelete && r.CanDelete).ToList();
            foreach (var r in deleteRows)
            {
                if (IsCategoryInUse(doc, r.CategoryId))
                {
                    blockedDeletes.Add(r.ToString());
                }
            }

            if (blockedDeletes.Count > 0)
            {
                // Block the entire apply and report — do not modify anything.
                req.Done(
                    $"Apply blocked. The following styles are still in use and cannot be deleted:\n" +
                    string.Join("\n", blockedDeletes.Select(s => $"  • {s}")),
                    blockedDeletes);
                return;
            }

            try
            {
                using var tx = new Transaction(doc, "BA · Apply Line Style Edits");
                tx.Start();

                foreach (var r in rows)
                {
                    if (!r.IsDirty) continue;

                    // Resolve the category from the document — do not trust the cached reference
                    var cat = GetCategoryById(doc, r.CategoryId);
                    if (cat == null)
                    {
                        errors.Add($"[{r}] Category not found in document.");
                        continue;
                    }

                    if (r.IsMarkedForDelete)
                    {
                        // Deletion was pre-validated above; proceed
                        try
                        {
                            doc.Delete(r.CategoryId);
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"[{r}] Delete failed: {ex.Message}");
                        }
                        // Skip graphic/rename changes — category is being deleted
                        continue;
                    }

                    bool changedAny = false;

                    // 1. Color
                    if (r.HasColorChange && r.IsEditable)
                    {
                        try
                        {
                            cat.LineColor = new Autodesk.Revit.DB.Color(r.Color.R, r.Color.G, r.Color.B);
                            changedAny = true;
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"[{r}] Color: {ex.Message}");
                        }
                    }

                    // 2. Line weight
                    if (r.HasWeightChange && r.IsEditable)
                    {
                        int w = Math.Clamp(r.LineWeight, 1, 16);
                        try
                        {
                            cat.SetLineWeight(w, GraphicsStyleType.Projection);
                            changedAny = true;
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"[{r}] Weight: {ex.Message}");
                        }
                    }

                    // 3. Pattern
                    if (r.HasPatternChange && r.IsEditable)
                    {
                        var patternId = r.ResolvedPatternId ?? ElementId.InvalidElementId;
                        try
                        {
                            cat.SetLinePatternId(patternId, GraphicsStyleType.Projection);
                            changedAny = true;
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"[{r}] Pattern: {ex.Message}");
                        }
                    }

                    // 4. Rename (last — safest order)
                    if (r.HasNameChange && r.CanRename)
                    {
                        var newName = r.StyleName?.Trim();
                        if (string.IsNullOrWhiteSpace(newName))
                        {
                            errors.Add($"[{r}] Rename skipped: name cannot be empty.");
                        }
                        else
                        {
                            try
                            {
                                var gs = cat.GetGraphicsStyle(GraphicsStyleType.Projection);
                                if (gs == null)
                                {
                                    errors.Add($"[{r}] Rename skipped: no projection GraphicsStyle found.");
                                }
                                else
                                {
                                    var gsElement = doc.GetElement(gs.Id);
                                    if (gsElement == null || gsElement.IsModifiable)
                                    {
                                        errors.Add($"[{r}] Rename skipped: GraphicsStyle element is read-only.");
                                    }
                                    else
                                    {
                                        gsElement.Name = newName;
                                        changedAny = true;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"[{r}] Rename: {ex.Message}");
                            }
                        }
                    }

                    if (changedAny) okCount++;
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                req.Done($"Transaction failed: {ex.Message}", errors);
                return;
            }

            var summary = $"Applied. Modified: {okCount}, Deleted: {deletedCount}, Errors: {errors.Count}";
            if (errors.Count > 0)
                summary += $" | First: {errors[0]}";

            req.Done(summary, errors);
        }

        public string GetName() => "BA.LineStyleHub.ExternalHandler";

        // ── Helpers ──────────────────────────────────────────────────────────

        private static bool IsCategoryInUse(Document doc, ElementId categoryId)
        {
            // A category is "in use" if any element in the document references it as its category.
            // We use a category filter which is the most direct approach.
            try
            {
                var filter = new ElementCategoryFilter(categoryId);
                var count = new FilteredElementCollector(doc)
                    .WherePasses(filter)
                    .GetElementCount();
                return count > 0;
            }
            catch
            {
                // If the filter throws (e.g. category not a valid element category),
                // assume it is in use to be safe.
                return true;
            }
        }

        private static Category? GetCategoryById(Document doc, ElementId id)
        {
            var settings = doc.Settings;
            foreach (Category parent in settings.Categories)
            {
                if (parent == null) continue;
                if (parent.Id == id) return parent;
                foreach (Category sub in parent.SubCategories)
                {
                    if (sub?.Id == id) return sub;
                }
            }
            return null;
        }
    }

    internal sealed class ApplyLineStyleEditsRequest
    {
        public IReadOnlyList<LineStyleRow> Rows { get; }
        public Action<string, IReadOnlyList<string>> Done { get; }

        public ApplyLineStyleEditsRequest(
            IReadOnlyList<LineStyleRow> rows,
            Action<string, IReadOnlyList<string>> done)
        {
            Rows = rows ?? throw new ArgumentNullException(nameof(rows));
            Done = done ?? throw new ArgumentNullException(nameof(done));
        }
    }
}
