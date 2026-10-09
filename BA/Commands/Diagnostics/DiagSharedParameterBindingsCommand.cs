using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.RoomClassification.Configuration;
using BA.RoomClassification.Models;

namespace BA.Commands.Diagnostics
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DiagSharedParameterBindingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            Dictionary<Guid, string> knownCatalog = BuildKnownCatalogLookup();
            List<BindingRow> rows = new List<BindingRow>();

            BindingMap map = doc.ParameterBindings;
            DefinitionBindingMapIterator it = map.ForwardIterator();
            it.Reset();

            while (it.MoveNext())
            {
                try
                {
                    Definition def = it.Key;
                    if (def == null)
                        continue;

                    Guid guid;
                    bool isShared;
                    TryResolveGuid(doc, def, out guid, out isShared);

                    string groupLabel;
                    try
                    {
                        groupLabel = LabelUtils.GetLabelForGroup(def.GetGroupTypeId());
                    }
                    catch
                    {
                        groupLabel = "(unresolved)";
                    }

                    Binding binding = (Binding)it.Current;
                    string bindingType = binding is InstanceBinding ? "Instance"
                        : binding is TypeBinding ? "Type"
                        : binding?.GetType().Name ?? "(null)";

                    string categories = "(none)";
                    if (binding is ElementBinding elementBinding)
                    {
                        categories = string.Join(", ",
                            elementBinding.Categories.Cast<Category>()
                                .Select(c => c.Name)
                                .OrderBy(n => n));
                    }

                    string catalogMatch;
                    if (isShared && knownCatalog.TryGetValue(guid, out string catalogName))
                    {
                        catalogMatch = catalogName == def.Name
                            ? "BA Room Classification catalog"
                            : $"BA Room Classification catalog (catalog name '{catalogName}' differs from bound name '{def.Name}')";
                    }
                    else if (isShared)
                    {
                        catalogMatch = "(not in any known BA catalog)";
                    }
                    else
                    {
                        catalogMatch = "";
                    }

                    rows.Add(new BindingRow
                    {
                        Name = def.Name,
                        IsShared = isShared,
                        GuidDisplay = isShared ? guid.ToString() : "N/A (project parameter, not shared)",
                        Group = groupLabel,
                        BindingType = bindingType,
                        Categories = categories,
                        CatalogMatch = catalogMatch
                    });
                }
                catch (Exception ex)
                {
                    rows.Add(new BindingRow
                    {
                        Name = "(error reading this binding entry)",
                        IsShared = false,
                        GuidDisplay = ex.Message,
                        Group = "",
                        BindingType = "",
                        Categories = "",
                        CatalogMatch = ""
                    });
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("BA shared and project parameter binding dump");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Document: {doc.Title}");
            sb.AppendLine(new string('=', 120));
            sb.AppendLine();

            int sharedCount = rows.Count(r => r.IsShared);
            int projectCount = rows.Count - sharedCount;
            int recognizedCount = rows.Count(r => r.CatalogMatch.StartsWith("BA Room Classification catalog"));

            sb.AppendLine($"Total bindings: {rows.Count}  | shared: {sharedCount}  | project, not shared: {projectCount}  | matched to a known BA catalog: {recognizedCount}");
            sb.AppendLine(new string('=', 120));
            sb.AppendLine();

            foreach (BindingRow row in rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"Name: {row.Name}");
                sb.AppendLine($"  Shared: {row.IsShared}    GUID: {row.GuidDisplay}");
                sb.AppendLine($"  Group: {row.Group}    Binding: {row.BindingType}");
                sb.AppendLine($"  Categories: {row.Categories}");
                if (!string.IsNullOrEmpty(row.CatalogMatch))
                    sb.AppendLine($"  Catalog: {row.CatalogMatch}");
                sb.AppendLine();
            }

            string outputPath = Path.Combine(Path.GetTempPath(), $"BA_SharedParameterBindings_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);

            TaskDialog td = new TaskDialog("BA: Shared Parameter Binding Dump")
            {
                MainInstruction = "Diagnostic complete",
                MainContent =
                    $"{rows.Count} parameter bindings found in this document.\n" +
                    $"{sharedCount} shared, {projectCount} project, not shared.\n" +
                    $"{recognizedCount} matched a known BA catalog.\n\n" +
                    $"Full dump written to:\n{outputPath}",
                CommonButtons = TaskDialogCommonButtons.Ok
            };
            td.Show();

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = outputPath,
                    UseShellExecute = true
                });
            }
            catch
            {
                // If no default text editor associated, user still has the path from the dialog.
            }

            return Result.Succeeded;
        }

        private static bool TryResolveGuid(Document doc, Definition def, out Guid guid, out bool isShared)
        {
            guid = Guid.Empty;
            isShared = false;

            if (def is ExternalDefinition externalDefinition)
            {
                guid = externalDefinition.GUID;
                isShared = true;
                return true;
            }

            if (def is InternalDefinition internalDefinition)
            {
                ElementId id = internalDefinition.Id;
                if (id.Value > 0)
                {
                    Element element = doc.GetElement(id);
                    if (element is SharedParameterElement sharedParameterElement)
                    {
                        guid = sharedParameterElement.GuidValue;
                        isShared = true;
                        return true;
                    }
                }
                return false;
            }

            return false;
        }

        // Cross references bound parameters against BA's own declared shared parameter
        // catalogs, so the dump flags which bindings are ones BA Tools itself expects versus
        // unrelated shared parameters from elsewhere. Add further catalogs here as they show up.
        private static Dictionary<Guid, string> BuildKnownCatalogLookup()
        {
            Dictionary<Guid, string> lookup = new Dictionary<Guid, string>();

            foreach (RoomClassificationParameterDefinition p in RoomClassificationParameterCatalog.BuildDefault())
            {
                if (p.Guid != Guid.Empty && !lookup.ContainsKey(p.Guid))
                    lookup[p.Guid] = p.Name;
            }

            return lookup;
        }

        private sealed class BindingRow
        {
            public string Name { get; set; }
            public bool IsShared { get; set; }
            public string GuidDisplay { get; set; }
            public string Group { get; set; }
            public string BindingType { get; set; }
            public string Categories { get; set; }
            public string CatalogMatch { get; set; }
        }
    }
}