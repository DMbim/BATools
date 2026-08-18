// BA/Core/FamilyParamUtils.cs
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace BA.Core
{
    public static partial class FamilyParamUtils // <- CHANGED: added 'partial' to match the second declaration
    {
        // ---------------- Find ----------------

        /// <summary>
        /// Case-insensitive lookup of a family parameter by name.
        /// Returns null when not found.
        /// </summary>
        public static FamilyParameter FindParameter(FamilyManager fm, string name)
        {
            if (fm == null || string.IsNullOrWhiteSpace(name)) return null;
            return fm.GetParameters()
                .FirstOrDefault(p => p.Definition.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        // ADD these three methods to BA/Core/FamilyParamUtils.cs
        // Place them in the public static class FamilyParamUtils, after FindParameter

        /// <summary>
        /// Wraps fm.RemoveParameter with a structured return so callers avoid bare try/catch.
        /// </summary>
        public static bool RemoveParameterSafe(
            FamilyManager fm, FamilyParameter fp, out string error)
        {
            error = null;
            try { fm.RemoveParameter(fp); return true; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>
        /// Wraps fm.RenameParameter. Validates uniqueness before attempting the rename.
        /// </summary>
        public static bool RenameParameterSafe(
            FamilyManager fm, FamilyParameter fp, string newName, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(newName))
            {
                error = "New name is empty.";
                return false;
            }
            if (FindParameter(fm, newName) != null)
            {
                error = $"A parameter named '{newName}' already exists.";
                return false;
            }
            try { fm.RenameParameter(fp, newName); return true; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>
        /// Returns all Dimension elements in a family document whose FamilyLabel
        /// is the given FamilyParameter. Used for delete-guard UI display.
        /// </summary>
        public static List<Dimension> FindDimensionAssociations(Document doc, FamilyParameter fp)
        {
            var result = new List<Dimension>();
            if (doc == null || fp == null) return result;
            try
            {
                foreach (var d in new FilteredElementCollector(doc)
                                      .OfClass(typeof(Dimension))
                                      .Cast<Dimension>())
                {
                    try
                    {
                        if (d.FamilyLabel?.Id == fp.Id)
                            result.Add(d);
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }
        // ---------------- Value Get/Set ----------------

        public static object GetParameterValue(FamilyManager fm, FamilyParameter fp)
        {
            if (fm?.CurrentType == null || fp == null) return null;

            return fp.StorageType switch
            {
                StorageType.Double => fm.CurrentType.AsDouble(fp),
                StorageType.Integer => fm.CurrentType.AsInteger(fp),
                StorageType.String => fm.CurrentType.AsString(fp),
                StorageType.ElementId => fm.CurrentType.AsElementId(fp),
                _ => null
            };
        }

        public static void SetParameterValue(FamilyManager fm, FamilyParameter fp, object value)
        {
            if (fm == null || fp == null || value == null) return;

            switch (value)
            {
                case double d: fm.Set(fp, d); break;
                case int i: fm.Set(fp, i); break;
                case string s: fm.Set(fp, s); break;
                case ElementId id: fm.Set(fp, id); break;
            }
        }

        // ---------------- Value capture / restore across family types ----------------

        /// <summary>
        /// Captures the value of fp in every FamilyType.
        /// Saves and restores fm.CurrentType so the active type is unchanged on return.
        /// </summary>
        public static Dictionary<string, object> CaptureValuesAcrossFamilyTypes(
            FamilyManager fm, FamilyParameter fp)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (fm == null || fp == null) return dict;

            var originalType = fm.CurrentType;
            try
            {
                foreach (FamilyType ft in fm.Types)
                {
                    try
                    {
                        fm.CurrentType = ft;
                        if (!ft.HasValue(fp)) continue;
                        dict[ft.Name] = GetParameterValue(fm, fp);
                    }
                    catch { }
                }
            }
            finally
            {
                fm.CurrentType = originalType;
            }

            return dict;
        }

        /// <summary>
        /// Restores values captured by CaptureValuesAcrossFamilyTypes into fp.
        /// Saves and restores fm.CurrentType.
        /// </summary>
        public static void RestoreValuesAcrossFamilyTypes(
            FamilyManager fm, FamilyParameter fp, Dictionary<string, object> values)
        {
            if (fm == null || fp == null || values == null || values.Count == 0) return;

            var originalType = fm.CurrentType;
            try
            {
                foreach (FamilyType ft in fm.Types)
                {
                    if (!values.TryGetValue(ft.Name, out var val) || val == null) continue;
                    try
                    {
                        fm.CurrentType = ft;
                        SetParameterValue(fm, fp, val);
                    }
                    catch { }
                }
            }
            finally
            {
                fm.CurrentType = originalType;
            }
        }

        // ---------------- Non-shared param creation (reflection-safe) ----------------

        /// <summary>
        /// Creates a non-shared family parameter using the Revit 2026 API overload
        /// AddParameter(string, ForgeTypeId, ForgeTypeId, bool) located via reflection.
        /// Required because older overloads use the deprecated BuiltInParameterGroup enum.
        /// </summary>
        public static FamilyParameter AddFamilyParameterCompat(
            FamilyManager fm,
            string name,
            ForgeTypeId groupTypeId,
            ForgeTypeId specTypeId,
            bool isInstance)
        {
            if (fm == null) throw new ArgumentNullException(nameof(fm));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (specTypeId == null) throw new InvalidOperationException("SpecTypeId is null.");
            groupTypeId ??= GroupTypeId.Data;

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
                throw new MissingMethodException(
                    "FamilyManager.AddParameter(string, ForgeTypeId, ForgeTypeId, bool) not found in this API version.");

            var created = mi.Invoke(fm, new object[] { name, groupTypeId, specTypeId, isInstance })
                          as FamilyParameter;

            if (created == null)
                throw new InvalidOperationException($"FamilyManager.AddParameter returned null for '{name}'.");

            return created;
        }

        // ---------------- Replacement by shared name ----------------

        /// <summary>
        /// Matches the family parameter in decision to a shared parameter definition and
        /// replaces it. Formula guard removed — parameters with formulas are handled;
        /// the formula is captured and restored on the new shared parameter.
        /// </summary>
        public static bool TryReplaceByName(
            UIApplication uiapp,
            FamilyManager fm,
            ParameterPreview decision,
            StringBuilder log)
        {
            if (uiapp == null || fm == null || decision == null)
            {
                log?.AppendLine("TryReplaceByName: invalid args.");
                return false;
            }

            SharedParamUtils.LoadSharedParameterFile(uiapp.Application);
            var lookup = SharedParamUtils.BuildExternalDefinitionLookup();

            string target = string.IsNullOrWhiteSpace(decision.MatchedShared)
                ? decision.Name
                : decision.MatchedShared;

            var extDef = SharedParamUtils.FindBestSharedDefinition(
                target, lookup, out string matchedName, minScore: 0.66);

            if (extDef == null)
            {
                log?.AppendLine($"No shared match for '{target}'.");
                return false;
            }

            var fpToReplace = FindParameter(fm, decision.Name);
            if (fpToReplace == null)
            {
                log?.AppendLine($"Param '{decision.Name}' not found.");
                return false;
            }

            if (!SpecUtils.AreSpecsCompatible(
                    fpToReplace.Definition.GetDataType(), extDef.GetDataType()))
            {
                log?.AppendLine($"Skip '{decision.Name}' (spec mismatch vs '{matchedName}').");
                return false;
            }

            bool ok = ReplaceWithSharedParameter(
                fm, fpToReplace, extDef, decision.DesiredIsInstance, log);

            if (ok)
                log?.AppendLine($"Matched '{decision.Name}' \u2192 shared '{matchedName}'.");

            return ok;
        }

        // ---------------- Core replacement ----------------

        /// <summary>
        /// Replaces a family parameter with the given shared ExternalDefinition.
        /// Captures and restores type values and formula.
        /// Applies the desired scope (isInstance) to the newly created parameter.
        /// </summary>
        public static bool ReplaceWithSharedParameter(
            FamilyManager fm,
            FamilyParameter oldFp,
            Definition def,
            bool desiredIsInstance,
            StringBuilder log)
        {
            try
            {
                if (def is not ExternalDefinition extDef)
                {
                    log?.AppendLine($"Definition '{def?.Name}' is not ExternalDefinition.");
                    return false;
                }

                if (!SpecUtils.AreSpecsCompatible(
                        oldFp.Definition.GetDataType(), extDef.GetDataType()))
                {
                    log?.AppendLine($"Skip '{oldFp.Definition.Name}' (spec mismatch).");
                    return false;
                }

                // Capture formula before rename (Revit may silently clear it on rename)
                string capturedFormula = null;
                try
                {
                    if (oldFp.CanAssignFormula && !string.IsNullOrWhiteSpace(oldFp.Formula))
                        capturedFormula = oldFp.Formula;
                }
                catch { }

                // Capture type values
                object oldValue = GetParameterValue(fm, oldFp);

                ForgeTypeId groupId = oldFp.Definition.GetGroupTypeId() ?? GroupTypeId.Data;
                string oldName = oldFp.Definition.Name;

                // Rename old param to a temp name so the shared param can take the same slot
                string finalTempName = oldName + "_OLD";
                int suffix = 1;
                while (fm.GetParameters()
                           .Any(p => p.Definition.Name.Equals(finalTempName,
                                       StringComparison.OrdinalIgnoreCase)))
                {
                    finalTempName = $"{oldName}_OLD_{suffix}";
                    suffix++;
                }

                fm.RenameParameter(oldFp, finalTempName);

                FamilyParameter newFp = fm.AddParameter(extDef, groupId, desiredIsInstance);

                if (oldValue != null)
                    SetParameterValue(fm, newFp, oldValue);

                // Restore formula
                if (!string.IsNullOrWhiteSpace(capturedFormula))
                {
                    try
                    {
                        if (newFp.CanAssignFormula)
                            fm.SetFormula(newFp, capturedFormula);
                        else
                            log?.AppendLine(
                                $"Formula not restored on '{extDef.Name}': CanAssignFormula = false.");
                    }
                    catch (Exception fEx)
                    {
                        log?.AppendLine(
                            $"Formula restore failed on '{extDef.Name}': {fEx.Message}");
                    }
                }

                // Remove old param
                try
                {
                    fm.RemoveParameter(oldFp);
                    log?.AppendLine(
                        $"Replaced '{oldName}' with shared '{extDef.Name}' (removed old).");
                }
                catch (Exception removeEx)
                {
                    log?.AppendLine(
                        $"Replaced '{oldName}' with shared '{extDef.Name}', " +
                        $"old kept as '{finalTempName}' ({removeEx.Message}).");
                }

                return true;
            }
            catch (Exception ex)
            {
                log?.AppendLine(
                    $"Replace failed for '{oldFp?.Definition?.Name}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Overload that preserves the original parameter scope.
        /// Used by legacy call sites that don't track desired scope.
        /// </summary>
        public static bool ReplaceWithSharedParameter(
            FamilyManager fm, FamilyParameter oldFp, Definition def, StringBuilder log)
            => ReplaceWithSharedParameter(fm, oldFp, def, oldFp?.IsInstance ?? true, log);

        // ---------------- Utilities ----------------

        public static bool MatchesExternalDefinition(FamilyParameter fp, Definition def)
            => fp != null && def != null &&
               fp.Definition.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase);

        public static bool HasFormula(FamilyParameter fp)
            => fp != null && !string.IsNullOrEmpty(fp.Formula);

        public static ForgeTypeId GetSpec(FamilyParameter fp)
            => fp?.Definition?.GetDataType();

        public static FamilyParameter AddParameter(FamilyManager fm, Definition def, bool isInstance)
        {
            if (def is not ExternalDefinition extDef)
                throw new ArgumentException($"Definition '{def?.Name}' is not ExternalDefinition.");
            return fm.AddParameter(extDef, GroupTypeId.Data, isInstance);
        }
    }

    public static class SpecUtils
    {
        public static bool AreSpecsCompatible(ForgeTypeId spec1, ForgeTypeId spec2)
        {
            if (spec1 == null || spec2 == null) return false;
            return spec1.Equals(spec2);
        }
    }

    internal static class FamilyManagerExtensions
    {
        public static IEnumerable<FamilyParameter> GetParameters(this FamilyManager fm)
        {
            if (fm == null) yield break;
            var set = fm.Parameters;
            if (set == null) yield break;
            foreach (FamilyParameter fp in set) yield return fp;
        }
    }
}