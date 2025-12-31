using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BA.Core
{
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

            string target = string.IsNullOrWhiteSpace(decision.NewName) ? decision.Name : decision.NewName;

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

            // Spec check
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

                // Cache old info
                object oldValue = GetParameterValue(fm, oldFp);
                bool isInstance = oldFp.IsInstance;

                // Group is a ForgeTypeId in RVT 2026; retrieve from Definition
                ForgeTypeId groupId = oldFp.Definition.GetGroupTypeId();
                if (groupId == null) groupId = GroupTypeId.Data; // default safety

                // Avoid collision – rename old first
                string oldName = oldFp.Definition.Name;
                string tempName = oldName + "__OLD";
                try { fm.RenameParameter(oldFp, tempName); } catch { /* best effort */ }

                // Add new shared parameter (ExternalDefinition, ForgeTypeId group, bool isInstance)
                FamilyParameter newFp = fm.AddParameter(extDef, groupId, isInstance);

                // Copy value
                if (oldValue != null) SetParameterValue(fm, newFp, oldValue);

                // Remove old (temp)
                try { fm.RemoveParameter(oldFp); } catch { /* best effort */ }

                log?.AppendLine($"Replaced '{oldName}' with shared '{extDef.Name}'.");
                return true;
            }
            catch (Exception ex)
            {
                log?.AppendLine($"Replace failed for '{oldFp?.Definition?.Name}': {ex.Message}");
                return false;
            }
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
