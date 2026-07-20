// BA/Core/HarmonizerEventHandler.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.Core
{
    public class HarmonizerEventHandler : IExternalEventHandler
    {
        public UIApplication UiApplication { get; set; }
        public UIDocument UiDocument { get; set; }
        public Document Document { get; set; }

        public List<ParameterPreview> Decisions { get; } = new();
        public StringBuilder Log { get; } = new();
        public string SharedParamOverridePath { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = Document ?? UiDocument?.Document ?? app.ActiveUIDocument?.Document;
                if (doc == null || !doc.IsFamilyDocument)
                {
                    TaskDialog.Show("Parameter Manager", "Active document is not a family document.");
                    return;
                }

                var fm = doc.FamilyManager;
                if (fm == null)
                {
                    TaskDialog.Show("Parameter Manager", "FamilyManager is unavailable.");
                    return;
                }

                try
                {
                    var overridePath = string.IsNullOrWhiteSpace(SharedParamOverridePath)
                        ? null : SharedParamOverridePath;
                    SharedParamUtils.LoadSharedParameterFile(app.Application, overridePath);
                }
                catch { }

                using (var tg = new TransactionGroup(doc, "Family Parameter Manager"))
                {
                    tg.Start();
                    foreach (var decision in Decisions)
                        ApplyDecision(doc, fm, decision);
                    tg.Assimilate();
                }

                TaskDialog.Show("Family Parameter Manager", Log.ToString());
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Family Parameter Manager - Error", ex.ToString());
            }
        }

        // ---------------- Main dispatch ----------------

        private void ApplyDecision(Document doc, FamilyManager fm, ParameterPreview p)
        {
            if (p == null) return;

            var source = FamilyParamUtils.FindParameter(fm, p.Name);
            if (source == null)
            {
                Log.AppendLine($"NOT FOUND: {p.Name}");
                return;
            }

            var action = (p.EffectiveAction ?? "").Trim();

            using var t = new Transaction(doc, $"Apply: {p.Name}");
            t.Start();

            try
            {
                // ---- DELETE: only case that exits early ----
                if (action.Equals("Delete", StringComparison.OrdinalIgnoreCase))
                {
                    if (source.IsBuiltIn())
                    {
                        Log.AppendLine($"DELETE BLOCKED (built-in): {p.Name}");
                        t.Commit(); return;
                    }
                    if (IsUsedByAnyDimensionLabel(doc, source, out var dimWhy))
                    {
                        Log.AppendLine($"DELETE BLOCKED (dimension label): {p.Name} | {dimWhy}");
                        t.Commit(); return;
                    }
                    if (IsReferencedByOtherFormulas(fm, source, out var refs))
                    {
                        Log.AppendLine(
                            $"DELETE BLOCKED (formula reference): {p.Name} | " +
                            $"Used by: {string.Join(", ", refs)}");
                        t.Commit(); return;
                    }

                    fm.RemoveParameter(source);
                    Log.AppendLine($"DELETE: {p.Name}");
                    t.Commit(); return;
                }

                // ---- All other actions: track which param to post-process ----
                FamilyParameter currentFp = null;
                bool replaceSucceeded = false;

                if (action.Equals("Keep", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(action))
                {
                    currentFp = source;
                    Log.AppendLine($"KEEP: {p.Name}");
                }
                else if (action.Equals("Rename", StringComparison.OrdinalIgnoreCase))
                {
                    RenameFamilyParam(fm, source, p);
                    var newName = (p.TargetName ?? "").Trim();
                    currentFp = FamilyParamUtils.FindParameter(fm, newName) ?? source;
                }
                else if (action.Equals("Replace", StringComparison.OrdinalIgnoreCase))
                {
                    // TryReplaceByName passes p.DesiredIsInstance to ReplaceWithSharedParameter
                    // so the new shared param is created with the correct scope.
                    replaceSucceeded = FamilyParamUtils.TryReplaceByName(UiApplication, fm, p, Log);
                    Log.AppendLine(
                        replaceSucceeded ? $"REPLACE OK: {p.Name}" : $"REPLACE SKIP: {p.Name}");

                    if (replaceSucceeded)
                        currentFp = FamilyParamUtils.FindParameter(fm, (p.MatchedShared ?? "").Trim());
                    else
                        currentFp = source; // original still exists; fall through to scope/formula
                }
                else
                {
                    Log.AppendLine($"UNKNOWN ACTION '{action}' for {p.Name} - skipped.");
                    currentFp = source;
                }

                // ---- Scope change ----
                // Skip for successful Replace: scope was already set during AddParameter call.
                // Apply for all other outcomes including failed Replace (original param is still there).
                if (currentFp != null && p.ScopeChangeNeeded && !replaceSucceeded)
                {
                    TryApplyScope(fm, currentFp, p.DesiredIsInstance, Log);
                }

                // ---- Formula change ----
                // For Replace: ReplaceWithSharedParameter already restored the original formula.
                // We only override here when the user actually edited the Formula field
                // (FormulaChanged = true means p.Formula != p.OriginalFormula).
                // For Keep/Rename: applies the formula change directly to the existing param.
                if (currentFp != null && p.FormulaChanged)
                {
                    ApplyFormula(fm, currentFp, p.Formula, Log);
                }

                t.Commit();
            }
            catch (Exception ex)
            {
                Log.AppendLine($"FAILED: {p.Name} ({action}) - {ex.Message}");
                try { t.RollBack(); } catch { }
            }
        }

        // ---------------- Scope ----------------

        private bool TryApplyScope(
            FamilyManager fm, FamilyParameter fp, bool makeInstance, StringBuilder log)
        {
            if (fp == null) return false;
            try
            {
                if (fp.IsInstance == makeInstance) return true;

                if (makeInstance) fm.MakeInstance(fp);
                else fm.MakeType(fp);

                log?.AppendLine(
                    $"SCOPE: {fp.Definition?.Name} \u2192 {(makeInstance ? "Instance" : "Type")}");
                return true;
            }
            catch (Exception ex)
            {
                log?.AppendLine(
                    $"SCOPE FAILED: {fp.Definition?.Name} \u2192 " +
                    $"{(makeInstance ? "Instance" : "Type")} ({ex.Message})");
                return false;
            }
        }

        // ---------------- Formula ----------------

        private static void ApplyFormula(
            FamilyManager fm, FamilyParameter fp, string formula, StringBuilder log)
        {
            if (!fp.CanAssignFormula)
            {
                log?.AppendLine(
                    $"FORMULA SKIP: '{fp.Definition.Name}' (CanAssignFormula = false)");
                return;
            }

            try
            {
                // Pass empty string to clear; Revit API: "Set formula to an empty string to clear."
                var toSet = (formula ?? "").Trim();
                fm.SetFormula(fp, string.IsNullOrEmpty(toSet) ? "" : toSet);
                log?.AppendLine(
                    $"FORMULA SET: '{fp.Definition.Name}' = " +
                    $"{(string.IsNullOrEmpty(toSet) ? "(cleared)" : toSet)}");
            }
            catch (Exception ex)
            {
                log?.AppendLine($"FORMULA FAILED: '{fp.Definition.Name}' - {ex.Message}");
            }
        }

        // ---------------- Rename ----------------

        private void RenameFamilyParam(FamilyManager fm, FamilyParameter source, ParameterPreview p)
        {
            var targetName = (p.TargetName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(targetName) ||
                targetName.Equals(source.Definition.Name, StringComparison.OrdinalIgnoreCase))
            {
                Log.AppendLine($"RENAME skipped (no target) for {p.Name}");
                return;
            }

            if (FamilyParamUtils.FindParameter(fm, targetName) != null)
            {
                Log.AppendLine($"RENAME BLOCKED: '{targetName}' already exists.");
                return;
            }

            fm.RenameParameter(source, targetName);
            Log.AppendLine($"RENAME: {p.Name} \u2192 {targetName}");
        }

        // ---------------- Guard checks ----------------

        private bool IsUsedByAnyDimensionLabel(
            Document doc, FamilyParameter fp, out string details)
        {
            details = "";
            try
            {
                foreach (var d in new FilteredElementCollector(doc)
                                      .OfClass(typeof(Dimension))
                                      .Cast<Dimension>())
                {
                    FamilyParameter label = null;
                    try { label = d.FamilyLabel; } catch { }

                    if (label != null && label.Id == fp.Id)
                    {
                        details =
                            $"Dimension label in view " +
                            $"'{doc.GetElement(d.OwnerViewId)?.Name ?? "<view>"}' " +
                            $"(DimensionId {d.Id.Value})";
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private bool IsReferencedByOtherFormulas(
            FamilyManager fm, FamilyParameter target, out List<string> referrers)
        {
            referrers = new List<string>();
            try
            {
                var targetName = target?.Definition?.Name;
                if (string.IsNullOrWhiteSpace(targetName)) return false;

                foreach (var p in fm.GetParameters())
                {
                    if (p == null || p.Id == target.Id) continue;
                    try
                    {
                        if (!p.CanAssignFormula) continue;
                        var f = p.Formula;
                        if (string.IsNullOrWhiteSpace(f)) continue;
                        if (f.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0)
                            referrers.Add(p.Definition?.Name ?? "<unnamed>");
                    }
                    catch { }
                }
            }
            catch { }
            return referrers.Count > 0;
        }

        // ---------------- Group change (future / internal) ----------------

        /// <summary>
        /// Recreates a parameter under a different group or scope by
        /// temp-renaming the old one, creating a new one, restoring values
        /// and formula, then removing the old one.
        /// Not currently wired to any UI action but kept for future group-change support.
        /// </summary>
        private FamilyParameter RecreateSameParameterDifferentGroup(
            FamilyManager fm,
            FamilyParameter source,
            ForgeTypeId targetGroupId,
            bool targetIsInstance,
            StringBuilder log)
        {
            if (fm == null) throw new ArgumentNullException(nameof(fm));
            if (source == null) throw new ArgumentNullException(nameof(source));
            targetGroupId ??= GroupTypeId.Data;

            if (source.IsBuiltIn())
                throw new InvalidOperationException(
                    $"Built-in parameter cannot be recreated: {source.Definition.Name}");

            var name = source.Definition.Name;
            var spec = SafeGetSpecTypeId(source);
            if (spec == null)
                throw new InvalidOperationException(
                    $"Cannot recreate '{name}': SpecTypeId is null.");

            var currentGroup = SafeGetGroupTypeId(source);
            if (ForgeTypeIdEquals(currentGroup, targetGroupId)) return source;

            var values = FamilyParamUtils.CaptureValuesAcrossFamilyTypes(fm, source);
            var formula = TryGetFormula(source);

            var tempName = name + "__OLD__" + Guid.NewGuid().ToString("N")[..6];
            fm.RenameParameter(source, tempName);

            FamilyParameter created;
            if (source.IsShared && source.Definition is ExternalDefinition extDef)
                created = fm.AddParameter(extDef, targetGroupId, targetIsInstance);
            else
                created = FamilyParamUtils.AddFamilyParameterCompat(
                    fm, name, targetGroupId, spec, targetIsInstance);

            FamilyParamUtils.RestoreValuesAcrossFamilyTypes(fm, created, values);

            if (!string.IsNullOrWhiteSpace(formula))
            {
                try { fm.SetFormula(created, formula); }
                catch (Exception ex)
                { log?.AppendLine($"FORMULA RESTORE FAILED: {name} ({ex.Message})"); }
            }

            fm.RemoveParameter(source);
            return created;
        }

        // ---------------- Private helpers ----------------

        private ForgeTypeId SafeGetSpecTypeId(FamilyParameter fp)
        {
            try { return fp?.Definition?.GetDataType(); }
            catch { return null; }
        }

        private ForgeTypeId SafeGetGroupTypeId(FamilyParameter fp)
        {
            try { return fp?.Definition?.GetGroupTypeId() ?? GroupTypeId.Data; }
            catch { return GroupTypeId.Data; }
        }

        private bool ForgeTypeIdEquals(ForgeTypeId a, ForgeTypeId b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return a.Equals(b);
        }

        private string TryGetFormula(FamilyParameter fp)
        {
            try { return fp?.CanAssignFormula == true ? fp.Formula : null; }
            catch { return null; }
        }

        public string GetName() => "BA Family Parameter Manager";
    }

    internal static class FamilyParameterExtensions
    {
        public static bool IsBuiltIn(this FamilyParameter fp)
        {
            try
            {
                return fp?.Definition is InternalDefinition idef &&
                       idef.BuiltInParameter != BuiltInParameter.INVALID;
            }
            catch { return false; }
        }
    }
}