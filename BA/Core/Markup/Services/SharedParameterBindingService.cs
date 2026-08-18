// BA/Core/Parameters/SharedParameterBindingService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace BA.Core.Parameters
{
    /// <summary>
    /// Single entry point for binding shared parameters to categories. Consolidates what used
    /// to be split across SharedParameterBindingService.EnsureBound and
    /// SharedParameterBinder.BindSharedParameter / BindSharedParameterByGuid.
    ///
    /// The old SharedParameterBinder routed through RevitApiCompat.InsertOrReinsert, which did
    ///     map.Insert(def, binding, groupId) || map.ReInsert(def, binding, groupId)
    /// BindingMap.Insert fails once a definition has any existing binding, so any second call
    /// fell through to ReInsert. ReInsert does not merge, it replaces the entire binding,
    /// category set included. Binding the same parameter to a new category after it was already
    /// bound to others silently dropped the previously bound categories. No exception, no log,
    /// the parameter just goes missing on elements of the categories that got dropped.
    ///
    /// This class never does a blind ReInsert. Every bind operation reads the existing binding
    /// first, checks the binding kind (Instance vs Type) matches, and if it does, unions the
    /// existing CategorySet with whatever new categories were requested before writing back.
    /// A binding-kind mismatch is surfaced as an exception, never silently resolved, since
    /// converting an existing binding could affect other categories or elements already relying
    /// on it.
    ///
    /// MUST be called from within an active Transaction on doc. Caller's responsibility.
    /// </summary>
    public static class SharedParameterBindingService
    {
        /// <summary>
        /// Ensures paramName is bound to category on doc, loading the definition from
        /// sharedParamFilePath/groupName if it isn't already. No-op if already correctly bound.
        /// This is the original single-category, BuiltInCategory-based overload.
        /// </summary>
        public static void EnsureBound(
            Document doc,
            string sharedParamFilePath,
            string groupName,
            string paramName,
            BuiltInCategory category,
            bool instanceBinding = true)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(sharedParamFilePath))
                throw new ArgumentException("Shared parameter file path is empty.", nameof(sharedParamFilePath));
            if (string.IsNullOrWhiteSpace(paramName))
                throw new ArgumentException("Parameter name is empty.", nameof(paramName));

            Category cat = Category.GetCategory(doc, category)
                ?? throw new InvalidOperationException(
                    $"Category '{category}' does not exist in this document.");

            Definition definition = SharedParameterFileReader.FindExternalDefinitionInGroup(
                doc.Application, sharedParamFilePath, groupName, paramName);

            BindOrExtendCategories(doc, definition, new[] { cat }, GroupTypeId.Data, instanceBinding);
        }

        /// <summary>
        /// Ensures defName (optionally verified against guidHint) is bound to categories on doc.
        /// Replaces SharedParameterBinder.BindSharedParameter. If not found and createIfMissing
        /// is true, creates the definition in group "BA" of the shared parameter file.
        /// </summary>
        public static void EnsureBound(
            Application app,
            Document doc,
            string sharedParamFilePath,
            string defName,
            Guid guidHint,
            ForgeTypeId groupId,
            bool isInstance,
            IList<Category> categories,
            bool createIfMissing)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(defName))
                throw new ArgumentException("Definition name is required.", nameof(defName));
            if (categories == null || categories.Count == 0)
                throw new ArgumentException("At least one category is required.", nameof(categories));

            ExternalDefinition extDef = SharedParameterFileReader.FindExternalDefinitionByName(
                app, sharedParamFilePath, defName);

            if (extDef == null && createIfMissing)
                extDef = SharedParameterFileReader.CreateExternalDefinition_String(
                    app, sharedParamFilePath, defName, "BA");

            if (extDef == null)
                throw new InvalidOperationException(
                    $"Shared parameter '{defName}' not found in the shared parameter file.");

            if (guidHint != Guid.Empty && extDef.GUID != guidHint)
                throw new InvalidOperationException(
                    $"GUID mismatch for '{defName}'. File GUID={extDef.GUID} vs expected={guidHint}");

            BindOrExtendCategories(doc, extDef, categories.ToList(), groupId ?? GroupTypeId.Data, isInstance);
        }

        /// <summary>
        /// Ensures the shared parameter identified by guid (with nameHint as fallback lookup and
        /// as the name used if createIfMissing needs to create it) is bound to categories on doc.
        /// Replaces SharedParameterBinder.BindSharedParameterByGuid.
        /// </summary>
        public static void EnsureBoundByGuid(
            Application app,
            Document doc,
            string sharedParamFilePath, 
            Guid guid,
            string nameHint,
            ForgeTypeId groupId,
            bool isInstance,
            IList<Category> categories,
            bool createIfMissing)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (guid == Guid.Empty) throw new ArgumentException("GUID is required.", nameof(guid));
            if (categories == null || categories.Count == 0)
                throw new ArgumentException("At least one category is required.", nameof(categories));

            ExternalDefinition extDef = SharedParameterFileReader.FindExternalDefinitionByGuid(
                app, sharedParamFilePath, guid);

            if (extDef == null && !string.IsNullOrWhiteSpace(nameHint))
                extDef = SharedParameterFileReader.FindExternalDefinitionByName(app, sharedParamFilePath, nameHint);

            if (extDef == null && createIfMissing && !string.IsNullOrWhiteSpace(nameHint))
                extDef = SharedParameterFileReader.CreateExternalDefinition_String(
                    app, sharedParamFilePath, nameHint, "BA");

            if (extDef == null)
                throw new InvalidOperationException(
                    $"Shared parameter not found in SP file. GUID={guid}, NameHint='{nameHint}'");

            if (extDef.GUID != guid)
                throw new InvalidOperationException(
                    $"GUID mismatch. Expected={guid}, Found={extDef.GUID} (Name='{extDef.Name}')");

            BindOrExtendCategories(doc, extDef, categories.ToList(), groupId ?? GroupTypeId.Data, isInstance);
        }

        /// <summary>
        /// Core safe-bind logic shared by all three public overloads. Never blindly ReInserts.
        /// If the definition is already bound:
        ///   - binding kind mismatch (Instance vs Type) throws, never auto-converted
        ///   - otherwise, unions the existing CategorySet with categoriesToEnsure and ReInserts
        ///     only if that union actually adds something new (true no-op if already fully bound)
        /// If not bound at all, Inserts a fresh binding covering exactly categoriesToEnsure.
        /// </summary>
        private static void BindOrExtendCategories(
            Document doc,
            Definition definition,
            IReadOnlyCollection<Category> categoriesToEnsure,
            ForgeTypeId groupId,
            bool instanceBinding)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            string paramName = definition.Name;
            BindingMap bindingMap = doc.ParameterBindings;

            DefinitionBindingMapIterator iterator = bindingMap.ForwardIterator();
            iterator.Reset();
            while (iterator.MoveNext())
            {
                Definition existingDef = iterator.Key;
                if (!string.Equals(existingDef.Name, paramName, StringComparison.Ordinal))
                    continue;

                if (iterator.Current is not ElementBinding existingBinding)
                    continue;

                bool isInstanceBinding = existingBinding is InstanceBinding;
                if (isInstanceBinding != instanceBinding)
                    throw new InvalidOperationException(
                        $"Shared parameter '{paramName}' is already bound as a " +
                        $"{(isInstanceBinding ? "Type" : "Instance")} binding, not " +
                        $"{(instanceBinding ? "Instance" : "Type")}. Cannot auto-resolve this " +
                        "conflict, it must be fixed manually in Manage > Project Parameters.");

                List<Category> missingCategories = categoriesToEnsure
                    .Where(c => !existingBinding.Categories.Contains(c))
                    .ToList();

                if (missingCategories.Count == 0)
                    return; // already fully bound to every requested category, true no-op

                CategorySet extendedSet = doc.Application.Create.NewCategorySet();
                foreach (Category existingCat in existingBinding.Categories)
                    extendedSet.Insert(existingCat);
                foreach (Category c in missingCategories)
                    extendedSet.Insert(c);

                ElementBinding extendedBinding = instanceBinding
                    ? doc.Application.Create.NewInstanceBinding(extendedSet)
                    : doc.Application.Create.NewTypeBinding(extendedSet);

                if (!bindingMap.ReInsert(existingDef, extendedBinding, groupId))
                    throw new InvalidOperationException(
                        $"Failed to extend the existing binding for shared parameter " +
                        $"'{paramName}' with {missingCategories.Count} new category/categories.");

                return;
            }

            // Not bound at all yet.
            CategorySet catSet = doc.Application.Create.NewCategorySet();
            foreach (Category c in categoriesToEnsure)
                catSet.Insert(c);

            ElementBinding binding = instanceBinding
                ? doc.Application.Create.NewInstanceBinding(catSet)
                : doc.Application.Create.NewTypeBinding(catSet);

            bool inserted = bindingMap.Insert(definition, binding, groupId);
            if (!inserted)
                throw new InvalidOperationException(
                    $"Revit rejected binding shared parameter '{paramName}'. This usually means " +
                    "the parameter's ParameterType is incompatible with one of the target " +
                    "categories, or a category does not support bound parameters.");
        }
    }
}