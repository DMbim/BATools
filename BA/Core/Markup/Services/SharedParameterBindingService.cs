// BA/Core/Parameters/SharedParameterBindingService.cs
using System;
using Autodesk.Revit.DB;

namespace BA.Core.Parameters
{
    /// <summary>
    /// Ensures a shared parameter is bound (InstanceBinding) to a given category, auto-binding
    /// it from a shared parameter file if it isn't already. Generic, not tied to any single
    /// feature, other BA_-prefixed parameter consumers can reuse this rather than each writing
    /// their own binding logic.
    ///
    /// MUST be called from within an active Transaction on doc. Caller's responsibility, same
    /// convention as ProjectSetService.SetProjectSetName.
    ///
    /// Does not support auto-fixing a parameter that is already bound as a TypeBinding when an
    /// InstanceBinding is requested (or vice versa); that conflict is surfaced as an exception
    /// rather than silently resolved, since converting an existing binding could affect other
    /// categories or elements already relying on it.
    /// </summary>
    public static class SharedParameterBindingService
    {
        /// <summary>
        /// Ensures paramName is bound to category on doc. No-op if already correctly bound.
        /// Throws InvalidOperationException with a specific, actionable message if:
        ///  - the shared parameter file can't be opened,
        ///  - the group or definition doesn't exist in the file (nothing to auto-bind, someone
        ///    has to add the definition to the file itself first),
        ///  - the parameter is already bound but as the wrong binding kind (Instance vs Type),
        ///  - the bind operation itself fails for any other reason.
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

            BindingMap bindingMap = doc.ParameterBindings;

            // ---- Already bound? ----
            DefinitionBindingMapIterator iterator = bindingMap.ForwardIterator();
            iterator.Reset();
            while (iterator.MoveNext())
            {
                Definition existingDef = iterator.Key;
                if (!string.Equals(existingDef.Name, paramName, StringComparison.Ordinal))
                    continue;

                if (iterator.Current is not ElementBinding existingBinding)
                    continue;

                bool alreadyBoundToCategory = existingBinding.Categories.Contains(cat);
                bool isInstanceBinding = existingBinding is InstanceBinding;

                if (alreadyBoundToCategory)
                {
                    if (isInstanceBinding != instanceBinding)
                        throw new InvalidOperationException(
                            $"Shared parameter '{paramName}' is already bound to category " +
                            $"'{category}', but as a {(isInstanceBinding ? "Type" : "Instance")} " +
                            $"binding, not {(instanceBinding ? "Instance" : "Type")}. Cannot " +
                            "auto-resolve this conflict, it must be fixed manually in " +
                            "Manage > Project Parameters.");

                    return; // correctly bound already, nothing to do
                }

                // Bound to other categories, but not this one, extend the existing binding
                // rather than creating a duplicate, second binding for the same definition.
                if (isInstanceBinding != instanceBinding)
                    throw new InvalidOperationException(
                        $"Shared parameter '{paramName}' is already bound elsewhere as a " +
                        $"{(isInstanceBinding ? "Type" : "Instance")} binding. Cannot extend it " +
                        $"to category '{category}' as {(instanceBinding ? "Instance" : "Type")}, " +
                        "binding kind must match across all categories for the same parameter.");

                CategorySet extendedSet = doc.Application.Create.NewCategorySet();
                foreach (Category existingCat in existingBinding.Categories)
                    extendedSet.Insert(existingCat);
                extendedSet.Insert(cat);

                ElementBinding extendedBinding = instanceBinding
                    ? doc.Application.Create.NewInstanceBinding(extendedSet)
                    : doc.Application.Create.NewTypeBinding(extendedSet);

                if (!bindingMap.ReInsert(existingDef, extendedBinding, GroupTypeId.Data))
                    throw new InvalidOperationException(
                        $"Failed to extend the existing binding for shared parameter " +
                        $"'{paramName}' to include category '{category}'.");

                return;
            }

            // ---- Not bound at all, load the definition from the shared parameter file ----
            string originalFile;
            try
            {
                originalFile = doc.Application.SharedParametersFilename;
            }
            catch
            {
                originalFile = string.Empty;
            }

            try
            {
                doc.Application.SharedParametersFilename = sharedParamFilePath;

                DefinitionFile defFile = doc.Application.OpenSharedParameterFile()
                    ?? throw new InvalidOperationException(
                        $"Could not open the shared parameter file at '{sharedParamFilePath}'. " +
                        "Verify the file exists and is accessible on the network.");

                DefinitionGroup group = defFile.Groups.get_Item(groupName)
                    ?? throw new InvalidOperationException(
                        $"Shared parameter group '{groupName}' was not found in " +
                        $"'{sharedParamFilePath}'.");

                Definition definition = group.Definitions.get_Item(paramName)
                    ?? throw new InvalidOperationException(
                        $"Shared parameter '{paramName}' is not defined in group " +
                        $"'{groupName}' of '{sharedParamFilePath}'. The definition itself is " +
                        "missing from the shared parameter file, this cannot be auto-fixed. " +
                        "Contact your BIM admin to add it before this feature can be used.");

                CategorySet catSet = doc.Application.Create.NewCategorySet();
                catSet.Insert(cat);

                ElementBinding binding = instanceBinding
                    ? doc.Application.Create.NewInstanceBinding(catSet)
                    : doc.Application.Create.NewTypeBinding(catSet);

                bool inserted = bindingMap.Insert(definition, binding, GroupTypeId.Data);
                if (!inserted)
                    throw new InvalidOperationException(
                        $"Revit rejected binding shared parameter '{paramName}' to category " +
                        $"'{category}'. This usually means the parameter's ParameterType is " +
                        "incompatible with the target category, or the category does not " +
                        "support bound parameters.");
            }
            finally
            {
                try
                {
                    doc.Application.SharedParametersFilename = originalFile;
                }
                catch
                {
                    // Best-effort restore of the session's shared parameter file pointer.
                    // Not worth failing the whole operation over.
                }
            }
        }
    }
}