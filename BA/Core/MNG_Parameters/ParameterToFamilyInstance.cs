// File: AddSharedParameterToSelectedFamiliesCommand.cs
// Revit 2025/2026 compatible (ForgeTypeId parameter groups)

using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.App.Commands.Parameters
{
    [Transaction(TransactionMode.Manual)]
    public class AddSharedParameterToSelectedFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            Application app = uiapp.Application;

            try
            {
                // 1) Pick family instances
                IList<Reference> picked = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new FamilyInstanceSelectionFilter(),
                    "Select family instances. The shared parameter will be added as an INSTANCE parameter to each unique family.");

                var instances = picked
                    .Select(r => doc.GetElement(r))
                    .OfType<FamilyInstance>()
                    .ToList();

                if (instances.Count == 0)
                {
                    TaskDialog.Show("BA", "No family instances selected.");
                    return Result.Cancelled;
                }

                // 2) Gather unique Families
                var families = instances
                    .Select(fi => fi.Symbol?.Family)
                    .Where(f => f != null)
                    .Distinct(new FamilyIdComparer())
                    .ToList();

                if (families.Count == 0)
                {
                    TaskDialog.Show("BA", "No valid families found from selection.");
                    return Result.Cancelled;
                }

                // 3) Input: shared parameter file + group + param name
                var input = SimpleInputDialog.Show(
                    title: "Add Shared Parameter to Families",
                    lines: new[]
                    {
                        ("Shared Parameter File Path", app.SharedParametersFilename ?? ""),
                        ("Group Name", "BA"),
                        ("Parameter Name", "BA_SNIM_UI")
                    });

                if (input == null) return Result.Cancelled;

                var (ok, data) = input.Value;
                if (!ok) return Result.Cancelled;

                string spPath = data.Line1?.Trim() ?? "";
                string groupName = data.Line2?.Trim() ?? "";
                string paramName = data.Line3?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(spPath) ||
                    string.IsNullOrWhiteSpace(groupName) ||
                    string.IsNullOrWhiteSpace(paramName))
                {
                    TaskDialog.Show("BA", "All inputs are required.");
                    return Result.Cancelled;
                }

                // 4) Resolve ExternalDefinition from shared parameter file
                ExternalDefinition extDef = SharedParameterDefinitionResolver.Resolve(app, spPath, groupName, paramName);
                if (extDef == null)
                {
                    TaskDialog.Show("BA", $"Could not find definition '{paramName}' in group '{groupName}'.");
                    return Result.Failed;
                }

                // Choose parameter group and instance/type behavior
                ForgeTypeId groupTypeId = GroupTypeId.Data; // you can map from your UI group picker later
                bool isInstance = true;                     // instance parameter inside family

                // 5) Add parameter to each family and reload
                int changed = 0;
                int skipped = 0;

                using (var tg = new TransactionGroup(doc, $"BA – Add '{paramName}' to {families.Count} family(ies)"))
                {
                    tg.Start();

                    foreach (var fam in families)
                    {
                        bool added = FamilyEditor.TryAddSharedParameterAndReload(
                            uiapp,
                            doc,
                            fam,
                            extDef,
                            groupTypeId,
                            isInstance,
                            out string status);

                        if (added) changed++;
                        else skipped++;
                    }

                    tg.Assimilate();
                }

                TaskDialog.Show("BA",
                    $"Processed families: {families.Count}\n" +
                    $"Changed: {changed}\n" +
                    $"Skipped/No change: {skipped}\n\n" +
                    $"Parameter: {paramName}");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }
    }

    internal class FamilyInstanceSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is FamilyInstance;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }

    internal class FamilyIdComparer : IEqualityComparer<Family>
    {
        public bool Equals(Family x, Family y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return x.Id.Value == y.Id.Value;
        }

        public int GetHashCode(Family obj) => (int)(obj?.Id.Value ?? 0);
    }

    internal static class SharedParameterDefinitionResolver
    {
        public static ExternalDefinition Resolve(Application app, string sharedParamFilePath, string groupName, string definitionName)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (string.IsNullOrWhiteSpace(sharedParamFilePath)) throw new ArgumentNullException(nameof(sharedParamFilePath));

            string old = app.SharedParametersFilename;

            try
            {
                app.SharedParametersFilename = sharedParamFilePath;

                DefinitionFile file = app.OpenSharedParameterFile();
                if (file == null) return null;

                DefinitionGroup group = file.Groups.get_Item(groupName);
                if (group == null) return null;

                Definition def = group.Definitions.get_Item(definitionName);
                return def as ExternalDefinition;
            }
            finally
            {
                app.SharedParametersFilename = old;
            }
        }
    }

    internal static class FamilyEditor
    {
        public static bool TryAddSharedParameterAndReload(
            UIApplication uiapp,
            Document projectDoc,
            Family family,
            ExternalDefinition extDef,
            ForgeTypeId groupTypeId,
            bool isInstance,
            out string status)
        {
            status = "";

            if (uiapp == null) throw new ArgumentNullException(nameof(uiapp));
            if (projectDoc == null) throw new ArgumentNullException(nameof(projectDoc));
            if (family == null) throw new ArgumentNullException(nameof(family));
            if (extDef == null) throw new ArgumentNullException(nameof(extDef));

            Document famDoc = projectDoc.EditFamily(family);
            if (famDoc == null || !famDoc.IsFamilyDocument)
            {
                status = "Could not open family for edit.";
                return false;
            }

            try
            {
                FamilyManager fm = famDoc.FamilyManager;
                if (fm == null)
                {
                    status = "FamilyManager not available.";
                    return false;
                }

                // Already exists?
                bool exists = fm.Parameters
                    .Cast<FamilyParameter>()
                    .Any(p =>
                    {
                        if (p.IsShared)
                        {
                            try
                            {
                                Guid g = p.GUID; // Guid in your API
                                if (g != Guid.Empty && g == extDef.GUID)
                                    return true;
                            }
                            catch
                            {
                                // fallback to name
                            }
                        }

                        return string.Equals(p.Definition?.Name, extDef.Name, StringComparison.OrdinalIgnoreCase);
                    });

                if (exists)
                {
                    status = "Parameter already exists in family.";
                    return false;
                }

                using (var t = new Transaction(famDoc, $"BA – Add {extDef.Name}"))
                {
                    t.Start();
                    fm.AddParameter(extDef, groupTypeId, isInstance);
                    t.Commit();
                }

                using (var t2 = new Transaction(projectDoc, $"BA – Reload family '{family.Name}'"))
                {
                    t2.Start();
                    famDoc.LoadFamily(projectDoc, new AlwaysOverwriteFamilyLoadOptions());
                    t2.Commit();
                }

                status = "Added and reloaded.";
                return true;
            }
            finally
            {
                try { famDoc.Close(false); } catch { }
            }
        }
    }

    internal class AlwaysOverwriteFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = false;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = false;
            return true;
        }
    }

    internal static class SimpleInputDialog
    {
        public sealed class InputResult
        {
            public string Line1 { get; set; }
            public string Line2 { get; set; }
            public string Line3 { get; set; }
        }

        public static (bool Ok, InputResult Value)? Show(string title, (string Label, string Default)[] lines)
        {
            // Minimal placeholder: returns defaults.
            var res = new InputResult
            {
                Line1 = lines.Length > 0 ? lines[0].Default : "",
                Line2 = lines.Length > 1 ? lines[1].Default : "",
                Line3 = lines.Length > 2 ? lines[2].Default : ""
            };

            TaskDialog.Show(title,
                "This is a placeholder input.\n" +
                "Wire this to your WPF picker.\n\n" +
                $"SharedParamFile: {res.Line1}\n" +
                $"Group: {res.Line2}\n" +
                $"Name: {res.Line3}");

            return (true, res);
        }
    }
}
