// File: BA.Classification/ClassificationEngine.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Core.Classification;

namespace BA.Classification
{
    public sealed class ClassificationRunResult
    {
        public ClassificationReport Report { get; }
        public List<string> Warnings { get; }
        public string? TraceCsvPath { get; }

        public ClassificationRunResult(ClassificationReport report, List<string> warnings, string? traceCsvPath)
        {
            Report = report;
            Warnings = warnings;
            TraceCsvPath = traceCsvPath;
        }
    }

    public sealed class ClassificationEngine
    {
        private readonly string _excelPath;
        private readonly List<ClassificationRule> _rules;
        private readonly BaClassCatalog _catalog;

        public ClassificationEngine(string excelPath)
        {
            _excelPath = excelPath ?? throw new ArgumentNullException(nameof(excelPath));
            _catalog = BaClassCatalog.Load(_excelPath, "BAClass");
            _rules = ExcelRuleLoader.LoadRules(_excelPath, "Rules_vNext");
        }

        /// <summary>
        /// Deterministic ordering:
        /// 1) RulePriority DESC (higher wins)
        /// 2) Specificity DESC
        /// 3) RowOrder ASC (Excel row order)
        ///
        /// Report semantics (Type-based, per your decision):
        /// - TotalTypes: unique ElementTypes present (have at least one instance)
        /// - ConsideredTypes: types whose category exists in enabled rules
        /// - SkippedNoCategory: type category not defined in rules (no enabled rules target it)
        /// - SkippedMissingParameters: no match, but at least one "almost match" failed due to missing required parameter
        /// - NoMatch: rules exist for category but none matched (not missing-param case)
        /// </summary>
        public ClassificationRunResult ClassifyTypes(Document doc, ClassificationMode mode, bool writeTrace = true)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var report = new ClassificationReport();
            var warnings = new List<string>();

            RuleEvaluator.PreprocessRules(doc, _rules, warnings);

            var enabledRules = _rules.Where(r => r.Enabled).ToList();

            // Deterministic rule order (stable)
            var orderedRules = enabledRules
                .OrderByDescending(r => r.RulePriority)
                .ThenByDescending(r => r.SpecificityScore)
                .ThenBy(r => r.RowOrder)
                .ToList();

            // Representative instance per type (deterministic)
            var repByType = BuildRepresentativeInstances(doc, orderedRules);

            // TotalTypes = all types that exist (have at least one instance)
            var presentTypeIds = repByType.Keys.OrderBy(id => id).ToList();
            report.TotalTypes = presentTypeIds.Count;

            // Build "categories present in rules" set (what you call 'defined in our rules')
            // If a rule has no category restriction -> treat as "all categories defined"
            bool anyGlobalCategoryRule = orderedRules.Any(r =>
                string.IsNullOrWhiteSpace(r.RevitCategory) || string.Equals(r.RevitCategoryMatchMode, "Any", StringComparison.OrdinalIgnoreCase));

            HashSet<ElementId> definedCategoryIds = new HashSet<ElementId>();
            if (!anyGlobalCategoryRule)
            {
                foreach (var r in orderedRules)
                {
                    if (r.ResolvedBuiltInCategoryInt.HasValue)
                    {
                        var bic = (BuiltInCategory)r.ResolvedBuiltInCategoryInt.Value;
                        var cat = Category.GetCategory(doc, bic);
                        if (cat != null) definedCategoryIds.Add(cat.Id);
                    }
                }
            }

            var tracePath = writeTrace
                ? Path.Combine(Path.GetDirectoryName(_excelPath) ?? "", $"BA_Classification_Trace_{DateTime.Now:yyyyMMdd_HHmmss}.csv")
                : null;

            using IRuleTraceSink? trace = writeTrace ? new CsvRuleTraceSink(tracePath!) : null;
            trace?.WriteHeader();

            var pnames = ClassificationConstants.DefaultParameterNames;

            using (var t = new Transaction(doc, "BA Classification (Deterministic)"))
            {
                t.Start();

                foreach (var typeId in presentTypeIds)
                {
                    var type = doc.GetElement(typeId) as ElementType;
                    if (type == null) continue;

                    var repInstId = repByType[typeId];
                    var repInst = repInstId != ElementId.InvalidElementId ? doc.GetElement(repInstId) : null;

                    var familyName = GetFamilyName(type);
                    var typeName = type.Name ?? "";
                    var catId = type.Category?.Id ?? ElementId.InvalidElementId;
                    var catName = type.Category?.Name ?? "<null>";

                    // 1) Decide if this type is "considered"
                    bool isConsidered = anyGlobalCategoryRule || (catId != ElementId.InvalidElementId && definedCategoryIds.Contains(catId));

                    if (!isConsidered)
                    {
                        // Your semantics: category not defined in rules
                        report.SkippedNoCategory++;
                        report.SkippedNoRulesForCategory++; // keep this in sync for backwards UI compatibility

                        trace?.WriteTypeTrace(new TypeTraceRow
                        {
                            TypeId = (int)BA.Core.Overhead.ElementIdValue.Of(typeId),
                            RepresentativeInstanceId = (int)BA.Core.Overhead.ElementIdValue.Of(repInstId),
                            Category = catName,
                            FamilyName = familyName,
                            TypeName = typeName,
                            WinnerRuleId = "",
                            WinnerTargetCode = "",
                            WinnerWhy = "SkippedNoCategory: category not defined in enabled rules",
                            MatchedRuleIds = "",
                            MatchedDetails = ""
                        });

                        continue;
                    }

                    report.ConsideredTypes++;

                    // 2) Evaluate rules
                    var matches = new List<RuleMatch>();
                    bool sawMissingParamAlmostMatch = false;
                    var missingParamDetails = new List<string>();

                    foreach (var rule in orderedRules)
                    {
                        // quick category prefilter if rules are category-bound (when possible)
                        if (!anyGlobalCategoryRule && rule.ResolvedBuiltInCategoryInt.HasValue && type.Category != null)
                        {
                            var bic = (BuiltInCategory)rule.ResolvedBuiltInCategoryInt.Value;
                            var rc = Category.GetCategory(doc, bic);
                            if (rc != null && rc.Id != type.Category.Id)
                                continue;
                        }

                        var outcome = RuleEvaluator.EvaluateRule(doc, type, repInst, rule, out var m);

                        if (outcome == RuleEvalOutcome.Matched && m != null)
                        {
                            matches.Add(m);
                            continue;
                        }

                        if (outcome == RuleEvalOutcome.FailedMissingParameter && m != null)
                        {
                            // This is your "SkippedMissingParameters" signal, but only if no match in the end.
                            sawMissingParamAlmostMatch = true;
                            missingParamDetails.Add($"{rule.RuleId}: {string.Join(" ; ", m.Checks)}");
                        }
                    }

                    // 3) Winner selection / skip reasons
                    if (matches.Count == 0)
                    {
                        if (sawMissingParamAlmostMatch)
                        {
                            report.SkippedMissingParameters++;
                            report.AddExampleMissingParams($"{(int)BA.Core.Overhead.ElementIdValue.Of(typeId)} | {catName} | {familyName} | {typeName}");

                            trace?.WriteTypeTrace(new TypeTraceRow
                            {
                                TypeId = (int)BA.Core.Overhead.ElementIdValue.Of(typeId),
                                RepresentativeInstanceId = (int)BA.Core.Overhead.ElementIdValue.Of(repInstId),
                                Category = catName,
                                FamilyName = familyName,
                                TypeName = typeName,
                                WinnerRuleId = "",
                                WinnerTargetCode = "",
                                WinnerWhy = "SkippedMissingParameters: at least one near-match required a parameter that was not found",
                                MatchedRuleIds = "",
                                MatchedDetails = string.Join(" || ", missingParamDetails.Take(8))
                            });
                        }
                        else
                        {
                            report.NoMatch++;
                            report.AddExampleNoMatch($"{(int)BA.Core.Overhead.ElementIdValue.Of(typeId)} | {catName} | {familyName} | {typeName}");

                            trace?.WriteTypeTrace(new TypeTraceRow
                            {
                                TypeId = (int)BA.Core.Overhead.ElementIdValue.Of(typeId),
                                RepresentativeInstanceId = (int)BA.Core.Overhead.ElementIdValue.Of(repInstId),
                                Category = catName,
                                FamilyName = familyName,
                                TypeName = typeName,
                                WinnerRuleId = "",
                                WinnerTargetCode = "",
                                WinnerWhy = "NoMatch: rules exist for category but none matched",
                                MatchedRuleIds = "",
                                MatchedDetails = ""
                            });
                        }

                        continue;
                    }

                    // Deterministic winner
                    var winner = matches
                        .OrderByDescending(m => m.Priority)
                        .ThenByDescending(m => m.Specificity)
                        .ThenBy(m => m.Rule.RowOrder)
                        .First();

                    var winnerRule = winner.Rule;

                    _catalog.TryGet(winnerRule.TargetLevelCode, out var classItem);

                    var winnerWhy =
                        $"Winner by PriorityDESC/SpecificityDESC/RowASC. " +
                        $"Winner={winnerRule.RuleId} P={winnerRule.RulePriority} S={winnerRule.SpecificityScore} Row={winnerRule.RowOrder}. " +
                        $"Competitors={matches.Count}.";

                    var matchedIds = string.Join(" | ",
                        matches.OrderByDescending(m => m.Priority)
                               .ThenByDescending(m => m.Specificity)
                               .ThenBy(m => m.Rule.RowOrder)
                               .Select(m => $"{m.Rule.RuleId}(P{m.Rule.RulePriority},S{m.Rule.SpecificityScore},R{m.Rule.RowOrder})"));

                    var details = string.Join(" || ",
                        matches.OrderByDescending(m => m.Priority)
                               .ThenByDescending(m => m.Specificity)
                               .ThenBy(m => m.Rule.RowOrder)
                               .Select(m => $"{m.Rule.RuleId}: " + string.Join(" ; ", m.Checks)));

                    trace?.WriteTypeTrace(new TypeTraceRow
                    {
                        TypeId = (int)BA.Core.Overhead.ElementIdValue.Of(typeId),
                        RepresentativeInstanceId = (int)BA.Core.Overhead.ElementIdValue.Of(repInstId),
                        Category = catName,
                        FamilyName = familyName,
                        TypeName = typeName,
                        WinnerRuleId = winnerRule.RuleId,
                        WinnerTargetCode = winnerRule.TargetLevelCode,
                        WinnerWhy = winnerWhy,
                        MatchedRuleIds = matchedIds,
                        MatchedDetails = details
                    });

                    // Apply
                    if (!TryApplyToType(type, pnames, winnerRule, classItem, mode, report))
                        continue;

                    report.Classified++;
                }

                t.Commit();
            }

            return new ClassificationRunResult(report, warnings, tracePath);
        }

        private static Dictionary<ElementId, ElementId> BuildRepresentativeInstances(Document doc, List<ClassificationRule> rules)
        {
            var bics = new HashSet<BuiltInCategory>();
            foreach (var r in rules)
            {
                if (r.ResolvedBuiltInCategoryInt.HasValue)
                    bics.Add((BuiltInCategory)r.ResolvedBuiltInCategoryInt.Value);
            }

            FilteredElementCollector collector;
            if (bics.Count > 0)
            {
                var filter = new ElementMulticategoryFilter(bics.ToList());
                collector = new FilteredElementCollector(doc).WherePasses(filter).WhereElementIsNotElementType();
            }
            else
            {
                collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            }

            var rep = new Dictionary<ElementId, ElementId>();

            foreach (var e in collector)
            {
                var tid = e.GetTypeId();
                if (tid == ElementId.InvalidElementId) continue;

                if (!rep.TryGetValue(tid, out var current))
                    rep[tid] = e.Id;
                else if (e.Id.Value < current.Value)
                    rep[tid] = e.Id;
            }

            return rep;
        }

        private static bool TryApplyToType(
            ElementType type,
            ClassificationParameterNames pnames,
            ClassificationRule winner,
            BaClassItem? classItem,
            ClassificationMode mode,
            ClassificationReport report)
        {
            var pCode = type.LookupParameter(pnames.Code);
            var pDomain = type.LookupParameter(pnames.Domain);
            var pGroup = type.LookupParameter(pnames.Group);
            var pSub = type.LookupParameter(pnames.Subcode);
            var pEn = type.LookupParameter(pnames.NameEn);
            var pCz = type.LookupParameter(pnames.NameCz);

            if (pCode == null || pDomain == null || pGroup == null || pSub == null || pEn == null || pCz == null)
            {
                report.SkippedMissingParameters++; // if your BA_ params are missing, it is still "missing parameters" in practice
                report.AddExampleMissingParams($"{type.Id} | {type.Category?.Name} | {type.FamilyName} | {type.Name}");
                return false;
            }

            if (mode == ClassificationMode.FillEmptyOnly && !BA.Core.Classification.ParameterUtils.IsEmpty(pCode))
            {
                report.SkippedAlreadyClassified++;
                return false;
            }

            if (pCode.IsReadOnly || pDomain.IsReadOnly || pGroup.IsReadOnly || pSub.IsReadOnly || pEn.IsReadOnly || pCz.IsReadOnly)
            {
                report.SkippedReadOnlyOrTypeMismatch++;
                return false;
            }

            var domain = winner.Domain ?? "";
            var group = winner.Group ?? "";
            var subcode = winner.Subcode ?? "";
            var code = winner.TargetLevelCode ?? "";

            var labelEn = classItem?.LabelEn ?? "";
            var labelCz = classItem?.LabelCz ?? "";

            BA.Core.Classification.ParameterUtils.SetString(pDomain, domain);
            BA.Core.Classification.ParameterUtils.SetString(pGroup, group);
            BA.Core.Classification.ParameterUtils.SetString(pCode, code);
            BA.Core.Classification.ParameterUtils.SetString(pEn, labelEn);
            BA.Core.Classification.ParameterUtils.SetString(pCz, labelCz);

            if (int.TryParse(subcode, out var si))
                BA.Core.Classification.ParameterUtils.SetIntOrString(pSub, si);
            else
                BA.Core.Classification.ParameterUtils.SetString(pSub, subcode);

            return true;
        }

        private static string GetFamilyName(ElementType type)
        {
            if (type is FamilySymbol fs) return fs.FamilyName ?? "";
            try
            {
                if (!string.IsNullOrWhiteSpace(type.FamilyName))
                    return type.FamilyName;
            }
            catch { }
            return "";
        }
    }
}
