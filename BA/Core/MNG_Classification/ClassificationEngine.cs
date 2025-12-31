using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BA.Classification
{
    public class ClassificationEngine
    {
        private readonly string _excelPath;
        private readonly List<ClassificationRule> _rules;

        public ClassificationEngine(string excelPath)
        {
            _excelPath = excelPath;
            _rules = ExcelRuleLoader.LoadRules(excelPath);
        }

        public void ClassifyAll(Document doc)
        {
            string logPath = Path.Combine(
                Path.GetDirectoryName(_excelPath),
                $"BA_Classification_Log_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            );

            using var writer = new StreamWriter(logPath);
            writer.WriteLine("ElementId,Category,FamilyName,TypeName,RuleId,TargetCode,Result,Reason");

            foreach (var e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                foreach (var rule in _rules)
                {
                    var (matched, reason) = RuleEvaluator.Evaluate(e, rule);
                    if (matched)
                    {
                        ApplyClassification(doc, e, rule);
                        writer.WriteLine($"{e.Id},{e.Category?.Name},{GetFam(e)},{GetTypeName(doc, e)},{rule.RuleId},{rule.TargetLevelCode},MATCH,{reason}");
                        if (rule.StopOnMatch) break;
                    }
                }
            }

            TaskDialog.Show("BA Classification", $"Classification finished.\nLog: {logPath}");
        }

        private void ApplyClassification(Document doc, Element e, ClassificationRule rule)
        {
            using (Transaction t = new Transaction(doc, "Apply BA Classification"))
            {
                t.Start();
                SetParameter(e, "BA_SpecCode", rule.TargetLevelCode);
                SetParameter(e, "BA_SpecLabel_EN", rule.TargetLabel_EN);
                SetParameter(e, "BA_SpecLabel_Local", rule.TargetLabel_Local);
                t.Commit();
            }
        }

        private static void SetParameter(Element e, string name, string value)
        {
            var p = e.LookupParameter(name);
            if (p != null && !p.IsReadOnly)
                p.Set(value ?? "");
        }

        private static string GetFam(Element e) =>
            e.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString() ?? "";

        private static string GetTypeName(Document doc, Element e) =>
            (doc.GetElement(e.GetTypeId()) as ElementType)?.Name ?? "";
    }
}
