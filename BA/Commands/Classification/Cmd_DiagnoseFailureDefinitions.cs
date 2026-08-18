// FILE: BA_Tools/Commands/Diagnostics/Cmd_DiagnoseFailureDefinitions.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.Commands.Diagnostics
{
    [Transaction(TransactionMode.ReadOnly)]
    public class Cmd_DiagnoseFailureDefinitions : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                TaskDialog.Show("Diagnose Failure Definitions", "No active document.");
                return Result.Cancelled;
            }

            Document doc = uiDoc.Document;

            Dictionary<Guid, string> map = BuildFailureDefinitionMap(out int fieldCount, out int propertyCount, out int nestedTypeCount);
            IList<FailureMessage> warnings = doc.GetWarnings();

            var distinctByGuid = new Dictionary<Guid, (FailureSeverity Severity, List<string> Descriptions, int Count)>();

            foreach (var w in warnings)
            {
                Guid guid = w.GetFailureDefinitionId().Guid;
                string desc = w.GetDescriptionText();

                if (distinctByGuid.TryGetValue(guid, out var existing))
                {
                    if (!existing.Descriptions.Contains(desc))
                        existing.Descriptions.Add(desc);
                    distinctByGuid[guid] = (existing.Severity, existing.Descriptions, existing.Count + 1);
                }
                else
                {
                    distinctByGuid[guid] = (w.GetSeverity(), new List<string> { desc }, 1);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Failure definition map dump {DateTime.Now:yyyy MM dd HH:mm:ss}");
            sb.AppendLine($"Document: {doc.Title}");

            Assembly asm = Assembly.GetExecutingAssembly();
            sb.AppendLine($"Executing assembly path: {asm.Location}");
            sb.AppendLine($"Executing assembly last write time: {File.GetLastWriteTime(asm.Location)}");
            sb.AppendLine($"Executing assembly version: {asm.GetName().Version}");

            sb.AppendLine(new string('=', 80));
            sb.AppendLine($"Nested types visited (recursive, all levels): {nestedTypeCount}");
            sb.AppendLine($"Total static fields scanned: {fieldCount}");
            sb.AppendLine($"Total static properties scanned: {propertyCount}");
            sb.AppendLine($"Total FailureDefinitionId members mapped: {map.Count}");

            // NEW: full JoinElementsFailures catalog, independent of what's live in this
            // model. Filters the already-built map to anything qualified under
            // BuiltInFailures.JoinElementsFailures, so this covers every join-related
            // failure Revit 2026 defines, not just whichever one this test model triggers.
            sb.AppendLine(new string('=', 80));
            sb.AppendLine("JOIN ELEMENTS FAILURES CATALOG (full set, live or not)");
            sb.AppendLine("Suggested action is a naive name-substring guess only. Confirm or correct each one.");
            sb.AppendLine(new string('-', 80));

            const string joinPrefix = "BuiltInFailures.JoinElementsFailures.";
            var joinEntries = map
                .Where(kvp => kvp.Value.StartsWith(joinPrefix, StringComparison.Ordinal))
                .OrderBy(kvp => kvp.Value)
                .ToList();

            if (joinEntries.Count == 0)
            {
                sb.AppendLine("  (no JoinElementsFailures nested type found under BuiltInFailures)");
            }
            else
            {
                foreach (var kvp in joinEntries)
                {
                    string memberName = kvp.Value.Substring(joinPrefix.Length);
                    string suggested = SuggestJoinAction(memberName);
                    bool isLive = distinctByGuid.ContainsKey(kvp.Key);

                    sb.AppendLine($"  {memberName}");
                    sb.AppendLine($"    GUID              : {kvp.Key}");
                    sb.AppendLine($"    Suggested action  : {suggested}");
                    sb.AppendLine($"    Live in this model: {(isLive ? $"YES ({distinctByGuid[kvp.Key].Count} occurrence(s))" : "no")}");
                    if (isLive)
                        sb.AppendLine($"    Description text(s): {string.Join(" | ", distinctByGuid[kvp.Key].Descriptions)}");
                    sb.AppendLine();
                }
            }

            sb.AppendLine(new string('=', 80));
            sb.AppendLine("ALL LIVE WARNINGS IN THIS MODEL");
            sb.AppendLine(new string('-', 80));

            foreach (var kvp in distinctByGuid.OrderByDescending(k => k.Value.Count))
            {
                string matchedName = map.TryGetValue(kvp.Key, out var name)
                    ? name
                    : "UNMATCHED (not found via BuiltInFailures reflection, may be a custom or family defined failure)";

                sb.AppendLine($"GUID: {kvp.Key}");
                sb.AppendLine($"  BuiltInFailures match : {matchedName}");
                sb.AppendLine($"  Severity              : {kvp.Value.Severity}");
                sb.AppendLine($"  Occurrences           : {kvp.Value.Count}");
                sb.AppendLine($"  Description text(s)   : {string.Join(" | ", kvp.Value.Descriptions)}");
                sb.AppendLine();
            }

            string outputFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BA", "Diagnostics");
            Directory.CreateDirectory(outputFolder);
            string outputPath = Path.Combine(outputFolder, $"FailureDefinitionMap_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(outputPath, sb.ToString());

            AppLogger.LogInfo($"Failure definition diagnostic written to {outputPath}");

            TaskDialog.Show("Diagnose Failure Definitions",
                $"{joinEntries.Count} join related failure type(s) catalogued (live or not).\n" +
                $"{distinctByGuid.Count} distinct warning type(s) currently live in this model.\n\n" +
                $"Full output written to:\n{outputPath}\n\n" +
                "Review the JOIN ELEMENTS FAILURES CATALOG section, confirm or correct each " +
                "suggested action, and send me the list so I can write the seed rules.");

            return Result.Succeeded;
        }

        // Naive, name-only heuristic. Never treat this as ground truth, it exists only
        // to save you from typing an action for every single entry by hand.
        private static string SuggestJoinAction(string memberName)
        {
            string n = memberName.ToLowerInvariant();

            if (n.Contains("disjoint"))
                return "Unjoin (guess: stale join relationship, no longer intersecting)";

            if (n.Contains("cannotjoin") || n.Contains("cannot"))
                return "Ignore, needs review (guess: join cannot be computed, likely a geometry or category conflict, not a stale relationship)";

            if (n.Contains("overlap"))
                return "Ignore, needs review (guess: overlapping solids is a different problem than a stale join)";

            if (n.Contains("order") || n.Contains("sequence"))
                return "Ignore, needs review (guess: join order/priority conflict, resolving this changes miter behavior, needs a human call)";

            return "Ignore, needs review (no clear keyword match, name alone isn't enough to guess safely)";
        }

        private static Dictionary<Guid, string> BuildFailureDefinitionMap(out int fieldCount, out int propertyCount, out int nestedTypeCount)
        {
            var map = new Dictionary<Guid, string>();
            Type rootType = typeof(BuiltInFailures);

            int fc = 0, pc = 0, ntc = 0;
            CollectRecursive(rootType, rootType.Name, map, ref fc, ref pc, ref ntc);

            fieldCount = fc;
            propertyCount = pc;
            nestedTypeCount = ntc;
            return map;
        }

        // Revit 2026 confirmed: BuiltInFailures exposes FailureDefinitionId via public
        // static get-only PROPERTIES, not fields, unlike prior API versions. Both are
        // scanned here so this keeps working if a future version mixes the two or
        // reverts, rather than silently going back to a zero count.
        private static void CollectRecursive(
            Type type,
            string qualifiedPrefix,
            Dictionary<Guid, string> map,
            ref int fieldCount,
            ref int propertyCount,
            ref int nestedTypeCount)
        {
            const BindingFlags staticFlags = BindingFlags.Public | BindingFlags.Static;

            FieldInfo[] fields = type.GetFields(staticFlags);
            fieldCount += fields.Length;

            foreach (FieldInfo f in fields)
            {
                if (f.FieldType == typeof(FailureDefinitionId) && f.GetValue(null) is FailureDefinitionId fdIdF)
                {
                    map[fdIdF.Guid] = $"{qualifiedPrefix}.{f.Name}";
                }
            }

            PropertyInfo[] props = type.GetProperties(staticFlags);
            propertyCount += props.Length;

            foreach (PropertyInfo p in props)
            {
                if (p.PropertyType == typeof(FailureDefinitionId) && p.GetIndexParameters().Length == 0 && p.CanRead)
                {
                    if (p.GetValue(null) is FailureDefinitionId fdIdP)
                    {
                        map[fdIdP.Guid] = $"{qualifiedPrefix}.{p.Name}";
                    }
                }
            }

            foreach (Type nestedType in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                nestedTypeCount++;
                CollectRecursive(nestedType, $"{qualifiedPrefix}.{nestedType.Name}", map, ref fieldCount, ref propertyCount, ref nestedTypeCount);
            }
        }
    }
}