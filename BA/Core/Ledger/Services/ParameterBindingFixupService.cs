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
    ///    via the existing BA.Core.SharedParamUtils (reused rather than duplicated), using
    ///    whatever Application.SharedParametersFilename currently points to, and bound fresh
    ///    with a fallback default group (GroupTypeId.Data). Change FallbackGroupForNewBindings
    ///    if you want a different default.
    ///
    /// Corrected from an earlier draft that used BuiltInParameterGroup and
    /// InternalDefinition.GetSharedParameterGUID(), neither of which exist in this Revit 2026
    /// SDK: BuiltInParameterGroup has been fully replaced by ForgeTypeId/GroupTypeId, and bound
    /// shared parameter Definitions are cast directly to ExternalDefinition instead.
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
        /// newly created), false if it genuinely could not be resolved (not found anywhere in
        /// the document's bindings, and not found in the shared parameter file either).
        /// </summary>
        public static bool EnsureParameterBound(Document doc, Category category, Guid parameterGuid)
        {
            if (doc == null || category == null || parameterGuid == Guid.Empty)
            {
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
                        AppLogger.LogInfo($"ParameterBindingFixupService: ReInsert failed widening binding for GUID {parameterGuid} to category '{category.Name}'.");
                        return false;
                    }

                    doc.Regenerate();
                    AppLogger.LogInfo($"ParameterBindingFixupService: widened existing binding for GUID {parameterGuid} to include category '{category.Name}'.");
                    return true;
                }

                // Not bound anywhere in this document at all.
                return CreateFreshBinding(doc, category, parameterGuid);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"ParameterBindingFixupService.EnsureParameterBound failed for GUID {parameterGuid} / category '{category?.Name}'", ex);
                return false;
            }
        }

        private static bool CreateFreshBinding(Document doc, Category category, Guid parameterGuid)
        {
            // Reuses the project's existing shared-parameter-file lookup utility rather than
            // duplicating file-opening/searching logic here. Passing null for
            // sharedParamFilePath and defName means: use whatever Application.
            // SharedParametersFilename is currently set to, and match purely by GUID.
            ExternalDefinition externalDefinition = BA.Core.SharedParamUtils.FindExternalDefinitionByGuidOrName(
                doc.Application, null, null, parameterGuid);

            if (externalDefinition == null)
            {
                AppLogger.LogInfo($"ParameterBindingFixupService: GUID {parameterGuid} not found in the current shared parameter file (or no shared parameter file configured).");
                return false;
            }

            CategorySet categorySet = doc.Application.Create.NewCategorySet();
            categorySet.Insert(category);

            TypeBinding typeBinding = doc.Application.Create.NewTypeBinding(categorySet);

            bool inserted = doc.ParameterBindings.Insert(externalDefinition, typeBinding, FallbackGroupForNewBindings);

            if (!inserted)
            {
                AppLogger.LogInfo($"ParameterBindingFixupService: Insert failed creating new binding for '{externalDefinition.Name}' ({parameterGuid}) on category '{category.Name}'.");
                return false;
            }

            doc.Regenerate();
            AppLogger.LogInfo($"ParameterBindingFixupService: created new Type binding for '{externalDefinition.Name}' ({parameterGuid}) on category '{category.Name}' using fallback group Data.");
            return true;
        }
    }
}
