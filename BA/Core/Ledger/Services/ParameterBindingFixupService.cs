using System;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BAApplication;

namespace BA.Core.Ledger
{
    /// <summary>
    /// Silently fixes up missing/mismatched shared parameter bindings encountered while
    /// applying a pulled Ledger value. Two cases:
    ///
    /// 1. Parameter already bound to the project, just not to this specific category -- widen
    ///    the existing binding's CategorySet. Reuses whatever binding type (Type/Instance) and
    ///    Parameter Group it already had; ReInsert does not require re-specifying the group.
    ///
    /// 2. Parameter not bound anywhere in the project at all -- there is, by definition, no
    ///    existing binding anywhere in this document to copy a group from. Looked up by GUID
    ///    via BA.Core.SharedParamUtils, using whatever Application.SharedParametersFilename
    ///    currently points to, and bound fresh with a fallback default group (GroupTypeId.Data).
    ///
    /// EnsureParameterBound now reports WHY it failed via failureReason, since "GUID not found
    /// in file" and "Insert rejected by Revit" are different problems with different fixes on
    /// the user's end (missing/mismatched shared parameter file vs. a name collision with an
    /// existing non-shared parameter of the same name in the target family). Collapsing both
    /// into one generic message made this undiagnosable from the sync dialog alone.
    ///
    /// Runs silently, no user-facing notification, per explicit instruction. Must be called
    /// from within an already-open Transaction on doc; this service does not open its own.
    /// </summary>
    public static class ParameterBindingFixupService
    {
        private static readonly ForgeTypeId FallbackGroupForNewBindings = GroupTypeId.Data;

        /// <summary>
        /// Ensures the shared parameter identified by parameterGuid is bound to category in
        /// doc. Returns true if it's now bound (whether it already was, was widened, or was
        /// newly created). Returns false if it genuinely could not be resolved; failureReason
        /// is populated with a specific, user-actionable explanation in that case.
        /// </summary>
        public static bool EnsureParameterBound(Document doc, Category category, Guid parameterGuid, out string failureReason)
        {
            failureReason = null;

            if (doc == null || category == null || parameterGuid == Guid.Empty)
            {
                failureReason = "Invalid document, category, or parameter GUID passed to EnsureParameterBound.";
                return false;
            }

            try
            {
                BindingMap bindingMap = doc.ParameterBindings;
                DefinitionBindingMapIterator iterator = bindingMap.ForwardIterator();
                iterator.Reset();

                while (iterator.MoveNext())
                {
                    Definition definition = iterator.Key;
                    if (!(definition is ExternalDefinition externalDefinition) || externalDefinition.GUID != parameterGuid)
                    {
                        continue;
                    }

                    if (!(iterator.Current is ElementBinding existingBinding))
                    {
                        failureReason = $"Parameter '{definition.Name}' is bound in this document under a non-element binding type, which this service does not handle.";
                        return false;
                    }

                    CategorySet existingCategories = existingBinding.Categories;
                    bool alreadyBound = existingCategories.Cast<Category>().Any(c => c.Id == category.Id);

                    if (alreadyBound)
                    {
                        return true; // already correctly bound, nothing to fix
                    }

                    existingCategories.Insert(category);
                    bool reInserted = bindingMap.ReInsert(definition, existingBinding);

                    if (!reInserted)
                    {
                        failureReason = $"Revit rejected widening the existing binding for '{definition.Name}' to include category '{category.Name}'.";
                        AppLogger.LogInfo($"ParameterBindingFixupService: {failureReason}");
                        return false;
                    }

                    doc.Regenerate();
                    AppLogger.LogInfo($"ParameterBindingFixupService: widened existing binding for GUID {parameterGuid} to include category '{category.Name}'.");
                    return true;
                }

                // Not bound anywhere in this document at all.
                return CreateFreshBinding(doc, category, parameterGuid, out failureReason);
            }
            catch (Exception ex)
            {
                failureReason = $"Unexpected error resolving binding: {ex.Message}";
                AppLogger.LogError($"ParameterBindingFixupService.EnsureParameterBound failed for GUID {parameterGuid} / category '{category?.Name}'", ex);
                return false;
            }
        }

        private static bool CreateFreshBinding(Document doc, Category category, Guid parameterGuid, out string failureReason)
        {
            failureReason = null;

            // Reuses the project's existing shared-parameter-file lookup utility rather than
            // duplicating file-opening/searching logic here. Passing null for
            // sharedParamFilePath and defName means: use whatever Application.
            // SharedParametersFilename is currently set to, and match purely by GUID.
            ExternalDefinition externalDefinition = BA.Core.SharedParamUtils.FindExternalDefinitionByGuidOrName(
                doc.Application, null, null, parameterGuid);

            if (externalDefinition == null)
            {
                failureReason = "This parameter's GUID was not found in the shared parameter file currently loaded in this Revit session (File > Options > Shared Parameters). Either the file is out of date, or a different file is configured than the one used when the parameter was originally created.";
                AppLogger.LogInfo($"ParameterBindingFixupService: GUID {parameterGuid} not found in the current shared parameter file (or no shared parameter file configured).");
                return false;
            }

            CategorySet categorySet = doc.Application.Create.NewCategorySet();
            categorySet.Insert(category);

            TypeBinding typeBinding = doc.Application.Create.NewTypeBinding(categorySet);

            bool inserted = doc.ParameterBindings.Insert(externalDefinition, typeBinding, FallbackGroupForNewBindings);

            if (!inserted)
            {
                failureReason = $"Revit rejected creating a binding for '{externalDefinition.Name}' on category '{category.Name}'. The most common cause is a name collision: a parameter named '{externalDefinition.Name}' already exists in this document (often a family-local Type Parameter that isn't marked Shared) and Revit will not bind a shared parameter under an already-occupied name.";
                AppLogger.LogInfo($"ParameterBindingFixupService: {failureReason}");
                return false;
            }

            doc.Regenerate();
            AppLogger.LogInfo($"ParameterBindingFixupService: created new Type binding for '{externalDefinition.Name}' ({parameterGuid}) on category '{category.Name}' using fallback group Data.");
            return true;
        }
    }
}