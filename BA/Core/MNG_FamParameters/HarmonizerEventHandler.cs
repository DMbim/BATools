// File: BA.Core/HarmonizerEventHandler.cs
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

                // Load shared param file once (only needed for Replace-to-shared)
                try
                {
                    var overridePath = string.IsNullOrWhiteSpace(SharedParamOverridePath) ? null : SharedParamOverridePath;
                    SharedParamUtils.LoadSharedParameterFile(app.Application, overridePath);
                }
                catch
                {
                    // ok - Replace might fail later, but other actions still work
                }

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

        private void ApplyDecision(Document doc, FamilyManager fm, ParameterPreview p)
        {
            if (p == null) return;

            var source = FindFamilyParam(fm, p.Name);
            if (source == null)
            {
                Log.AppendLine($"NOT FOUND: {p.Name}");
                return;
            }

            var action = (p.Action ?? "").Trim();
            ForgeTypeId desiredGroup = string.IsNullOrWhiteSpace(p.GroupTypeId)
                ? GroupTypeId.Data
                : new ForgeTypeId(p.GroupTypeId);

            bool desiredIsInstance = p.DesiredIsInstance;

            using var t = new Transaction(doc, $"Apply: {p.Name}");
            t.Start();

            try
            {
                // 1) DELETE
                if (action.Equals("Delete", StringComparison.OrdinalIgnoreCase))
                {
                    if (source.IsBuiltIn())
                    {
                        Log.AppendLine($"DELETE BLOCKED (built-in): {p.Name}");
                        t.Commit();
                        return;
                    }

                    fm.RemoveParameter(source);
                    Log.AppendLine($"DELETE: {p.Name}");
                    t.Commit();
                    return;
                }

                // 2) Scope switch (in-place)
                TryApplyScope(fm, source, desiredIsInstance, Log);

                // 3) Group change (recreate) - this may replace "source" with new param object
                // 3) Group change (recreate) - this may replace "source" with new param object
                desiredGroup = string.IsNullOrWhiteSpace(p.GroupTypeId)
                    ? GroupTypeId.Data
                    : new ForgeTypeId(p.GroupTypeId);

                var currentGroup = SafeGetGroupTypeId(source);

                if (!ForgeTypeIdEquals(desiredGroup, currentGroup))
                {
                    source = RecreateSameParameterDifferentGroup(fm, source, desiredGroup, desiredIsInstance, Log);

                    // use the UI-friendly name if available, else fall back to TypeId
                    var label = !string.IsNullOrWhiteSpace(p.GroupName) ? p.GroupName : p.GroupTypeId;
                    Log.AppendLine($"GROUP: {p.Name} -> {label}");
                }

                // 4) Keep / Rename / Replace
                if (action.Equals("Keep", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(action))
                {
                    Log.AppendLine($"KEEP: {p.Name}");
                    t.Commit();
                    return;
                }

                if (action.Equals("Rename", StringComparison.OrdinalIgnoreCase))
                {
                    RenameFamilyParam(fm, source, p);
                    t.Commit();
                    return;
                }

                if (action.Equals("Replace", StringComparison.OrdinalIgnoreCase))
                {
                    bool ok = FamilyParamUtils.TryReplaceByName(UiApplication, fm, p, Log);
                    Log.AppendLine(ok ? $"REPLACE OK: {p.Name}" : $"REPLACE SKIP: {p.Name}");
                    t.Commit();
                    return;
                }

                Log.AppendLine($"UNKNOWN ACTION '{p.Action}' for {p.Name} - skipped.");
                t.Commit();
            }
            catch (Exception ex)
            {
                Log.AppendLine($"FAILED: {p.Name} ({action}) - {ex.Message}");
                t.RollBack();
            }
        }

        // ---------------- Core operations ----------------

        private bool TryApplyScope(FamilyManager fm, FamilyParameter fp, bool makeInstance, StringBuilder log)
        {
            if (fp == null) return false;

            try
            {
                if (fp.IsInstance == makeInstance)
                    return true;

                if (makeInstance) fm.MakeInstance(fp);
                else fm.MakeType(fp);

                log?.AppendLine($"SCOPE: {fp.Definition?.Name} -> {(makeInstance ? "Instance" : "Type")}");
                return true;
            }
            catch (Exception ex)
            {
                log?.AppendLine($"SCOPE FAILED: {fp.Definition?.Name} -> {(makeInstance ? "Instance" : "Type")} ({ex.Message})");
                return false;
            }
        }

        /// <summary>
        /// Change group/placement by recreating the parameter (only reliable method in Revit).
        /// Keeps same name, spec, shared-ness, and scope, and copies values per family type + formula best-effort.
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
            if (targetGroupId == null) targetGroupId = GroupTypeId.Data;

            if (source.IsBuiltIn())
                throw new InvalidOperationException($"Built-in parameter cannot be recreated: {source.Definition.Name}");

            var name = source.Definition.Name;
            var spec = SafeGetSpecTypeId(source);
            if (spec == null)
                throw new InvalidOperationException($"Cannot recreate '{name}': SpecTypeId is null.");

            var currentGroup = SafeGetGroupTypeId(source);
            if (ForgeTypeIdEquals(currentGroup, targetGroupId))
                return source;

            // Capture values + formula
            var values = CaptureValuesAcrossFamilyTypes(fm, source);
            var formula = TryGetFormula(source);

            // Rename old out of the way so we can create new with same name
            var tempName = name + "__OLD__" + Guid.NewGuid().ToString("N").Substring(0, 6);
            fm.RenameParameter(source, tempName);

            // Create new param (shared vs non-shared)
            FamilyParameter created;
            if (source.IsShared && source.Definition is ExternalDefinition extDef)
            {
                created = fm.AddParameter(extDef, targetGroupId, targetIsInstance);
            }
            else
            {
                created = AddFamilyParameterCompat(fm, name, targetGroupId, spec, targetIsInstance);
            }

            // Restore values
            RestoreValuesAcrossFamilyTypes(fm, created, values);

            // Restore formula best-effort
            if (!string.IsNullOrWhiteSpace(formula))
            {
                try { fm.SetFormula(created, formula); }
                catch (Exception ex) { log?.AppendLine($"FORMULA RESTORE FAILED: {name} ({ex.Message})"); }
            }

            // Remove old
            fm.RemoveParameter(source);

            return created;
        }

        private void RenameFamilyParam(FamilyManager fm, FamilyParameter source, ParameterPreview p)
        {
            var targetName =
                !string.IsNullOrWhiteSpace(p.NewName) ? p.NewName :
                !string.IsNullOrWhiteSpace(p.MatchedShared) ? p.MatchedShared :
                null;

            if (string.IsNullOrWhiteSpace(targetName) ||
                targetName.Equals(source.Definition.Name, StringComparison.OrdinalIgnoreCase))
            {
                Log.AppendLine($"RENAME skipped (no target) for {p.Name}");
                return;
            }

            var existing = FindFamilyParam(fm, targetName);
            if (existing != null)
            {
                Log.AppendLine($"RENAME BLOCKED: '{targetName}' already exists.");
                return;
            }

            fm.RenameParameter(source, targetName);
            Log.AppendLine($"RENAME: {p.Name} -> {targetName}");
        }

        // ---------------- Value capture/restore ----------------

        private Dictionary<string, object> CaptureValuesAcrossFamilyTypes(FamilyManager fm, FamilyParameter fp)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var originalType = fm.CurrentType;

            try
            {
                foreach (FamilyType ft in fm.Types)
                {
                    fm.CurrentType = ft;
                    if (!ft.HasValue(fp)) continue;

                    dict[ft.Name] = FamilyParamUtils.GetParameterValue(fm, fp);
                }
            }
            finally
            {
                fm.CurrentType = originalType;
            }

            return dict;
        }

        private void RestoreValuesAcrossFamilyTypes(FamilyManager fm, FamilyParameter fp, Dictionary<string, object> values)
        {
            if (values == null || values.Count == 0) return;

            var originalType = fm.CurrentType;

            try
            {
                foreach (FamilyType ft in fm.Types)
                {
                    if (!values.TryGetValue(ft.Name, out var val) || val == null)
                        continue;

                    fm.CurrentType = ft;
                    FamilyParamUtils.SetParameterValue(fm, fp, val);
                }
            }
            finally
            {
                fm.CurrentType = originalType;
            }
        }

        private string TryGetFormula(FamilyParameter fp)
        {
            try
            {
                if (fp != null && fp.CanAssignFormula)
                    return fp.Formula;
            }
            catch { }
            return null;
        }

        // ---------------- Find + compat helpers ----------------

        private FamilyParameter FindFamilyParam(FamilyManager fm, string name)
        {
            if (fm == null || string.IsNullOrWhiteSpace(name)) return null;

            return fm.GetParameters()
                .FirstOrDefault(x => x.Definition.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

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

        /// <summary>
        /// Non-shared family param creation (API version-safe via reflection).
        /// </summary>
        private FamilyParameter AddFamilyParameterCompat(FamilyManager fm, string name, ForgeTypeId groupTypeId, ForgeTypeId specTypeId, bool isInstance)
        {
            if (fm == null) throw new ArgumentNullException(nameof(fm));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (groupTypeId == null) groupTypeId = GroupTypeId.Data;
            if (specTypeId == null) throw new InvalidOperationException("SpecTypeId is null.");

            var mi = fm.GetType().GetMethods()
                .FirstOrDefault(m =>
                {
                    if (m.Name != "AddParameter") return false;
                    var ps = m.GetParameters();
                    return ps.Length == 4
                        && ps[0].ParameterType == typeof(string)
                        && ps[1].ParameterType == typeof(ForgeTypeId)
                        && ps[2].ParameterType == typeof(ForgeTypeId)
                        && ps[3].ParameterType == typeof(bool);
                });

            if (mi == null)
                throw new MissingMethodException("FamilyManager.AddParameter(string, ForgeTypeId, ForgeTypeId, bool) not found.");

            var created = mi.Invoke(fm, new object[] { name, groupTypeId, specTypeId, isInstance }) as FamilyParameter;
            if (created == null) throw new InvalidOperationException("Failed to create family parameter.");

            return created;
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
            catch
            {
                return false;
            }
        }
    }
}