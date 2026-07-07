using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BA.Core
{
    /// <summary>
    /// Describes a dimension in the family that labels (drives via a parameter)
    /// a given FamilyParameter. Used for report-only warnings before Replace/Delete.
    /// </summary>
    public sealed class DimensionAssociationInfo
    {
        public ElementId DimensionId { get; set; } = ElementId.InvalidElementId;

        /// <summary>
        /// Names of the references (ref planes, lines, faces) the dimension runs between,
        /// where derivable. May be empty if names could not be resolved.
        /// </summary>
        public List<string> ReferenceNames { get; set; } = new();

        /// <summary>
        /// True if the dimension is a multi-segment dimension and the parameter was found
        /// via Dimension.FamilyLabels rather than the single Dimension.FamilyLabel.
        /// </summary>
        public bool IsMultiSegment { get; set; }

        public override string ToString()
        {
            string refs = ReferenceNames.Count > 0
                ? string.Join(" / ", ReferenceNames)
                : "(references not resolved)";

            return IsMultiSegment
                ? $"Dimension {DimensionId.Value} [multi-segment]: {refs}"
                : $"Dimension {DimensionId.Value}: {refs}";
        }
    }

    public static class FamilyParamUtils
    {

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

        // ---------------- Dimension association scan (report-only) ----------------

        /// <summary>
        /// Scans all Dimension elements in the family document (project view dimensions,
        /// i.e. dimensions placed in family sketch views) and returns those whose
        /// FamilyLabel or FamilyLabels reference the given FamilyParameter.
        ///
        /// Report-only: does not modify any dimension or association. Callers should
        /// log the result before and after Replace/Delete so the user knows which
        /// dimensions/ref planes were driven by the parameter being changed.
        ///
        /// Wrapped defensively: Dimension.FamilyLabel / FamilyLabels and Dimension.References
        /// can throw on dimensions in unusual states (EQ-constrained, formula-driven,
        /// detached). Any such dimension is skipped rather than aborting the whole scan.
        /// </summary>
        public static List<DimensionAssociationInfo> FindDimensionAssociations(Document doc, FamilyParameter fp)
        {
            var results = new List<DimensionAssociationInfo>();
            if (doc == null || fp == null || !doc.IsFamilyDocument) return results;

            var dimensions = new FilteredElementCollector(doc)
                .OfClass(typeof(Dimension))
                .Cast<Dimension>();

            foreach (var dim in dimensions)
            {
                try
                {
                    bool matchedSingle = false;
                    bool matchedMulti = false;

                    // Single-segment dimensions
                    try
                    {
                        var label = dim.FamilyLabel;
                        if (label != null && SameParameter(label, fp))
                            matchedSingle = true;
                    }
                    catch
                    {
                        // FamilyLabel can throw on multi-segment dimensions; fall through to FamilyLabels.
                    }

                    // Multi-segment (EQ) dimensions
                    if (!matchedSingle)
                    {
                        try
                        {
                            var label = dim.FamilyLabel;
                            if (label != null && SameParameter(label, fp))
                            {
                                matchedMulti = true;
                            }
                        }
                        catch
                        {
                            // No FamilyLabels on this Revit build / dimension type, ignore.
                        }
                    }

                    if (!matchedSingle && !matchedMulti) continue;

                    var info = new DimensionAssociationInfo
                    {
                        DimensionId = dim.Id,
                        IsMultiSegment = matchedMulti
                    };

                    try
                    {
                        var refArray = dim.References;
                        if (refArray != null)
                        {
                            foreach (Reference r in refArray)
                            {
                                try
                                {
                                    var refElem = doc.GetElement(r);
                                    if (refElem == null) continue;

                                    string name = !string.IsNullOrWhiteSpace(refElem.Name)
                                        ? refElem.Name
                                        : refElem.Category?.Name ?? refElem.GetType().Name;

                                    info.ReferenceNames.Add(name);
                                }
                                catch
                                {
                                    // Skip references that cannot be resolved to an element.
                                }
                            }
                        }
                    }
                    catch
                    {
                        // dim.References can throw on certain dimension states; leave ReferenceNames empty.
                    }

                    results.Add(info);
                }
                catch
                {
                    // Any other unexpected failure on this dimension: skip it, do not abort the scan.
                }
            }

            return results;
        }

        private static bool SameParameter(FamilyParameter a, FamilyParameter b)
        {
            if (a == null || b == null) return false;
            if (a.Id != null && b.Id != null && a.Id.Equals(b.Id)) return true;
            return a.Definition?.Name?.Equals(b.Definition?.Name, StringComparison.OrdinalIgnoreCase) == true
                   && a.IsInstance == b.IsInstance;
        }

        // ---------------- Replacement by shared name ----------------

        public static bool TryReplaceByName(
            UIApplication uiapp,
            FamilyManager fm,
            BA.Core.ParameterPreview decision,
            StringBuilder log)
        {
            if (uiapp == null || fm == null || decision == null)
            {
                log?.AppendLine("Invalid args.");
                return false;
            }

            SharedParamUtils.LoadSharedParameterFile(uiapp.Application);
            var lookup = SharedParamUtils.BuildExternalDefinitionLookup();

            string target = string.IsNullOrWhiteSpace(decision.MatchedShared)
                ? decision.Name
                : decision.MatchedShared;

            string matchedName;
            var extDef = SharedParamUtils.FindBestSharedDefinition(target, lookup, out matchedName, minScore: 0.66);
            if (extDef == null)
            {
                log?.AppendLine($"No shared match for '{target}'.");
                return false;
            }

            var fpToReplace = fm.GetParameters()
                                .FirstOrDefault(p => p.Definition.Name.Equals(decision.Name, StringComparison.OrdinalIgnoreCase));
            if (fpToReplace == null)
            {
                log?.AppendLine($"Param '{decision.Name}' not found.");
                return false;
            }

            var a = fpToReplace.Definition.GetDataType();
            var b = extDef.GetDataType();
            if (!SpecUtils.AreSpecsCompatible(a, b))
            {
                log?.AppendLine($"Skip '{decision.Name}' (spec mismatch vs '{matchedName}').");
                return false;
            }

            if (HasFormula(fpToReplace))
            {
                log?.AppendLine($"Skip '{decision.Name}' (has formula).");
                return false;
            }

            bool ok = ReplaceWithSharedParameter(fm, fpToReplace, extDef, log);
            if (ok) log?.AppendLine($"Matched '{decision.Name}' → shared '{matchedName}'.");
            return ok;
        }

        // ---------------- Core replacement (RVT 2026 API) ----------------

        public static bool ReplaceWithSharedParameter(FamilyManager fm, FamilyParameter oldFp, Definition def, StringBuilder log)
        {
            try
            {
                if (def is not ExternalDefinition extDef)
                {
                    log?.AppendLine($"Definition '{def?.Name}' is not external/shared.");
                    return false;
                }

                if (HasFormula(oldFp))
                {
                    log?.AppendLine($"Skip '{oldFp.Definition.Name}' (has formula).");
                    return false;
                }

                if (!SpecUtils.AreSpecsCompatible(oldFp.Definition.GetDataType(), extDef.GetDataType()))
                {
                    log?.AppendLine($"Skip '{oldFp.Definition.Name}' (spec mismatch).");
                    return false;
                }

                object oldValue = GetParameterValue(fm, oldFp);
                bool isInstance = oldFp.IsInstance;

                ForgeTypeId groupId = oldFp.Definition.GetGroupTypeId();
                if (groupId == null) groupId = GroupTypeId.Data;

                string oldName = oldFp.Definition.Name;
                string tempOldName = oldName + "_OLD";

                // Ensure unique temp name
                string finalTempName = tempOldName;
                int suffix = 1;
                while (fm.GetParameters().Any(p => p.Definition.Name.Equals(finalTempName, StringComparison.OrdinalIgnoreCase)))
                {
                    finalTempName = tempOldName + "_" + suffix;
                    suffix++;
                }

                fm.RenameParameter(oldFp, finalTempName);

                FamilyParameter newFp = fm.AddParameter(extDef, groupId, isInstance);

                if (oldValue != null)
                    SetParameterValue(fm, newFp, oldValue);

                try
                {
                    fm.RemoveParameter(oldFp);
                    log?.AppendLine($"Replaced '{oldName}' with shared '{extDef.Name}' and removed old parameter.");
                }
                catch (Exception removeEx)
                {
                    log?.AppendLine(
                        $"Replaced '{oldName}' with shared '{extDef.Name}', but old parameter was kept as '{finalTempName}' " +
                        $"because Revit would not remove it ({removeEx.Message}).");
                }

                return true;
            }
            catch (Exception ex)
            {
                log?.AppendLine($"Replace failed for '{oldFp?.Definition?.Name}': {ex.Message}");
                return false;
            }
        }

        // ---------------- Delete ----------------

        /// <summary>
        /// Removes a FamilyParameter. Logs any dimensions that were labeled by this
        /// parameter before removal (report-only, dimensions revert to unlabeled
        /// after RemoveParameter, Revit does not raise an event for this).
        /// </summary>
        public static bool RemoveParameterSafe(Document doc, FamilyManager fm, FamilyParameter fp, StringBuilder log)
        {
            if (doc == null || fm == null || fp == null)
            {
                log?.AppendLine("RemoveParameter: invalid args.");
                return false;
            }

            string name = fp.Definition?.Name ?? "<unknown>";

            if (HasFormula(fp))
            {
                log?.AppendLine($"Skip delete '{name}' (has formula; remove the formula first or delete manually).");
                return false;
            }

            var associations = FindDimensionAssociations(doc, fp);
            if (associations.Count > 0)
            {
                log?.AppendLine($"WARNING: '{name}' labels {associations.Count} dimension(s). These will become unlabeled after delete:");
                foreach (var a in associations)
                    log?.AppendLine($"   {a}");
            }

            try
            {
                fm.RemoveParameter(fp);
                log?.AppendLine($"Deleted '{name}'.");
                return true;
            }
            catch (Exception ex)
            {
                log?.AppendLine($"Delete failed for '{name}': {ex.Message}");
                return false;
            }
        }

        // ---------------- Rename (non-shared target) ----------------

        /// <summary>
        /// Renames a FamilyParameter to a plain (non-shared) name. Does not touch
        /// shared/external definitions; if the family parameter is currently shared
        /// and the target name does not correspond to a shared definition, the result
        /// is a shared parameter instance with a different display name bound to the
        /// same underlying definition (Revit allows renaming the FamilyParameter
        /// label independently of the ExternalDefinition.Name in some cases) — but
        /// more commonly for non-shared params this is a straightforward rename.
        /// Logs dimension associations (informational only, renaming does not break
        /// FamilyLabel bindings, the Id is unchanged).
        /// </summary>
        public static bool RenameParameterSafe(Document doc, FamilyManager fm, FamilyParameter fp, string newName, StringBuilder log)
        {
            if (doc == null || fm == null || fp == null)
            {
                log?.AppendLine("RenameParameter: invalid args.");
                return false;
            }

            string oldName = fp.Definition?.Name ?? "<unknown>";
            newName = (newName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(newName))
            {
                log?.AppendLine($"Skip rename '{oldName}' (target name is empty).");
                return false;
            }

            if (newName.Equals(oldName, StringComparison.OrdinalIgnoreCase))
            {
                log?.AppendLine($"Skip rename '{oldName}' (target equals current name).");
                return false;
            }

            if (fm.GetParameters().Any(p => !ReferenceEquals(p, fp) && p.Definition.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            {
                log?.AppendLine($"Skip rename '{oldName}' -> '{newName}' (a parameter with that name already exists).");
                return false;
            }

            // Informational: renaming does not detach FamilyLabel/FamilyLabels associations
            // (the FamilyParameter.Id is unchanged), but report so the user can verify.
            var associations = FindDimensionAssociations(doc, fp);
            if (associations.Count > 0)
            {
                log?.AppendLine($"'{oldName}' labels {associations.Count} dimension(s) (will remain associated after rename):");
                foreach (var a in associations)
                    log?.AppendLine($"   {a}");
            }

            try
            {
                fm.RenameParameter(fp, newName);
                log?.AppendLine($"Renamed '{oldName}' -> '{newName}'.");
                return true;
            }
            catch (Exception ex)
            {
                log?.AppendLine($"Rename failed for '{oldName}' -> '{newName}': {ex.Message}");
                return false;
            }
        }

        // ---------------- Favorites: GUID resolution ----------------

        /// <summary>
        /// Resolves the shared parameter GUID a row should be keyed on for favorites:
        /// - If the row's underlying FamilyParameter is already shared, returns its ExternalDefinition GUID.
        /// - Otherwise, if TargetName/MatchedShared/Name resolves against the given lookup, returns that GUID.
        /// - Returns Guid.Empty if no shared definition can be resolved.
        /// </summary>
        public static Guid ResolveSharedGuid(FamilyManager fm, ParameterPreview row, Dictionary<string, Definition> lookup)
        {
            if (fm == null || row == null) return Guid.Empty;

            if (row.IsShared)
            {
                var fp = fm.GetParameters()
                           .FirstOrDefault(p => p.Definition.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));

                if (fp?.Definition is ExternalDefinition extDef)
                    return extDef.GUID;
            }

            if (lookup == null) return Guid.Empty;

            string candidate = !string.IsNullOrWhiteSpace(row.TargetName) ? row.TargetName
                              : !string.IsNullOrWhiteSpace(row.MatchedShared) ? row.MatchedShared
                              : row.Name;

            if (lookup.TryGetValue(candidate, out var def) && def is ExternalDefinition extDef2)
                return extDef2.GUID;

            return Guid.Empty;
        }

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

            // Default group: Data (ForgeTypeId)
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
