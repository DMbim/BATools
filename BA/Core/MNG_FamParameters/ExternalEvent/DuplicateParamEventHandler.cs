// BA/Core/DuplicateParamEventHandler.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.Core
{
    /// <summary>
    /// ExternalEvent handler that duplicates selected family parameters.
    /// Creates non-shared copies of shared parameters (same GUID cannot be duplicated).
    /// Copies all type values and formula.
    /// After execution, invokes OnComplete on the UI thread with the list of new names.
    /// </summary>
    public class DuplicateParamEventHandler : IExternalEventHandler
    {
        public List<ParameterPreview> SourceParams { get; } = new();
        public Document Document { get; set; }
        public StringBuilder Log { get; } = new();

        /// <summary>
        /// Invoked on the WPF Dispatcher thread after execution.
        /// Receives list of new parameter names; null entries indicate failure for that param.
        /// </summary>
        public Action<List<string>> OnComplete { get; set; }

        public void Execute(UIApplication app)
        {
            Log.Clear();
            var doc = Document ?? app.ActiveUIDocument?.Document;

            if (doc == null || !doc.IsFamilyDocument)
            {
                Log.AppendLine("ERROR: Active document is not a family document.");
                NotifyComplete(null);
                return;
            }

            var fm = doc.FamilyManager;
            var newNames = new List<string>();

            using (var tg = new TransactionGroup(doc, "Duplicate Family Parameters"))
            {
                tg.Start();

                foreach (var p in SourceParams)
                {
                    string newName = null;

                    using (var t = new Transaction(doc, $"Duplicate '{p.Name}'"))
                    {
                        t.Start();
                        try
                        {
                            var fp = FamilyParamUtils.FindParameter(fm, p.Name);
                            if (fp == null)
                            {
                                Log.AppendLine($"NOT FOUND: '{p.Name}' - skipped.");
                                t.RollBack();
                                newNames.Add(null);
                                continue;
                            }

                            var spec = fp.Definition.GetDataType();
                            if (spec == null)
                            {
                                Log.AppendLine($"SKIP: '{p.Name}' - SpecTypeId is null.");
                                t.RollBack();
                                newNames.Add(null);
                                continue;
                            }

                            ForgeTypeId groupId;
                            try { groupId = fp.Definition.GetGroupTypeId() ?? GroupTypeId.Data; }
                            catch { groupId = GroupTypeId.Data; }

                            bool isInstance = fp.IsInstance;
                            var values = FamilyParamUtils.CaptureValuesAcrossFamilyTypes(fm, fp);

                            string formula = null;
                            try
                            {
                                if (fp.CanAssignFormula && !string.IsNullOrWhiteSpace(fp.Formula))
                                    formula = fp.Formula;
                            }
                            catch { }

                            newName = GenerateUniqueName(fm, p.Name);

                            // Shared params cannot be duplicated with the same GUID.
                            // Always create a non-shared copy regardless of source.
                            FamilyParameter newFp = FamilyParamUtils.AddFamilyParameterCompat(
                                fm, newName, groupId, spec, isInstance);

                            FamilyParamUtils.RestoreValuesAcrossFamilyTypes(fm, newFp, values);

                            if (!string.IsNullOrWhiteSpace(formula) && newFp.CanAssignFormula)
                            {
                                try { fm.SetFormula(newFp, formula); }
                                catch (Exception fEx)
                                { Log.AppendLine($"Formula restore failed for '{newName}': {fEx.Message}"); }
                            }

                            t.Commit();
                            newNames.Add(newName);
                            Log.AppendLine($"DUPLICATED: '{p.Name}' \u2192 '{newName}'");
                        }
                        catch (Exception ex)
                        {
                            Log.AppendLine($"DUPLICATE FAILED: '{p.Name}' - {ex.Message}");
                            try { t.RollBack(); } catch { }
                            newNames.Add(null);
                        }
                    }
                }

                tg.Assimilate();
            }

            NotifyComplete(newNames);
        }

        private static string GenerateUniqueName(FamilyManager fm, string baseName)
        {
            var existing = new HashSet<string>(
                fm.GetParameters().Select(x => x.Definition.Name),
                StringComparer.OrdinalIgnoreCase);

            var candidate = baseName + "_Copy";
            int n = 1;
            while (existing.Contains(candidate))
            {
                candidate = $"{baseName}_Copy{n}";
                n++;
            }
            return candidate;
        }

        private void NotifyComplete(List<string> names)
        {
            if (OnComplete == null) return;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                () => OnComplete(names ?? new List<string>()));
        }

        public string GetName() => "BA Duplicate Family Parameters";
    }
}