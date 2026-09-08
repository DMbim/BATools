// File: BA.Classification/RuleEvaluator.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace BA.Classification
{
    public enum ParamScope
    {
        Any,
        Instance,
        Type
    }

    public enum RuleEvalOutcome
    {
        Matched,
        Failed,
        FailedMissingParameter
    }

    public sealed class RuleMatch
    {
        public ClassificationRule Rule { get; }
        public List<string> Checks { get; } = new();

        public int Priority => Rule.RulePriority;
        public int Specificity => Rule.SpecificityScore;

        public RuleMatch(ClassificationRule rule) => Rule = rule;
    }

    public static class RuleEvaluator
    {
        public static void PreprocessRules(Document doc, List<ClassificationRule> rules, List<string> warnings)
        {
            foreach (var r in rules)
            {
                r.ResolvedBuiltInCategoryInt = null;

                if (!string.IsNullOrWhiteSpace(r.RevitCategory)
                    && Enum.TryParse(r.RevitCategory.Trim(), true, out BuiltInCategory bic))
                {
                    r.ResolvedBuiltInCategoryInt = (int)bic;
                }

                r.SpecificityScore = ComputeSpecificity(r);

                if (!string.IsNullOrWhiteSpace(r.RevitCategory) && r.ResolvedBuiltInCategoryInt == null)
                {
                    warnings?.Add($"Rule {r.RuleId}: RevitCategory='{r.RevitCategory}' not parsed as BuiltInCategory; will fallback to name match.");
                }
            }
        }

        private static int ComputeSpecificity(ClassificationRule r)
        {
            int score = 0;

            bool Has(string s) => !string.IsNullOrWhiteSpace(s);

            if (Has(r.RevitCategory) && !IsAny(r.RevitCategoryMatchMode)) score += 10;
            if (Has(r.FamilyName) && !IsAny(r.FamilyMatchMode)) score += 8;
            if (Has(r.TypeName) && !IsAny(r.TypeMatchMode)) score += 8;

            if (Has(r.ParameterName))
            {
                score += 9;
                if (Has(r.Operator)) score += 2;
                if (Has(r.ParameterValue1)) score += 3;
                if (Has(r.ParameterValue2)) score += 1;
            }

            return score;
        }

        /// <summary>
        /// Evaluates a rule for a TYPE (and an optional representative instance for instance-scope parameters).
        /// Returns Matched / Failed / FailedMissingParameter.
        /// MissingParameter only occurs if category/family/type checks passed but the required parameter was not found.
        /// </summary>
        public static RuleEvalOutcome EvaluateRule(
            Document doc,
            ElementType type,
            Element? representativeInstance,
            ClassificationRule rule,
            out RuleMatch? match)
        {
            match = new RuleMatch(rule);

            // 1) Category
            if (!IsAny(rule.RevitCategoryMatchMode) && !string.IsNullOrWhiteSpace(rule.RevitCategory))
            {
                if (rule.ResolvedBuiltInCategoryInt.HasValue)
                {
                    var bic = (BuiltInCategory)rule.ResolvedBuiltInCategoryInt.Value;
                    var cat = Category.GetCategory(doc, bic);

                    bool ok = (cat != null && type.Category != null && type.Category.Id == cat.Id);
                    match.Checks.Add($"Category: TypeCat='{type.Category?.Name}' vs Rule='{rule.RevitCategory}' => {(ok ? "OK" : "FAIL")}");
                    if (!ok) return RuleEvalOutcome.Failed;
                }
                else
                {
                    bool ok = StringMatch(type.Category?.Name ?? "", rule.RevitCategory, rule.RevitCategoryMatchMode);
                    match.Checks.Add($"CategoryName: '{type.Category?.Name}' {rule.RevitCategoryMatchMode} '{rule.RevitCategory}' => {(ok ? "OK" : "FAIL")}");
                    if (!ok) return RuleEvalOutcome.Failed;
                }
            }

            // 2) Family
            var familyName = GetFamilyName(type);
            if (!IsAny(rule.FamilyMatchMode) && !string.IsNullOrWhiteSpace(rule.FamilyName))
            {
                bool ok = StringMatch(familyName, rule.FamilyName, rule.FamilyMatchMode);
                match.Checks.Add($"Family: '{familyName}' {rule.FamilyMatchMode} '{rule.FamilyName}' => {(ok ? "OK" : "FAIL")}");
                if (!ok) return RuleEvalOutcome.Failed;
            }

            // 3) Type
            var typeName = type.Name ?? "";
            if (!IsAny(rule.TypeMatchMode) && !string.IsNullOrWhiteSpace(rule.TypeName))
            {
                bool ok = StringMatch(typeName, rule.TypeName, rule.TypeMatchMode);
                match.Checks.Add($"Type: '{typeName}' {rule.TypeMatchMode} '{rule.TypeName}' => {(ok ? "OK" : "FAIL")}");
                if (!ok) return RuleEvalOutcome.Failed;
            }

            // 4) Parameter
            if (!string.IsNullOrWhiteSpace(rule.ParameterName))
            {
                var scope = ParseScope(rule.ParameterScope);

                var (p, scopeUsed) = GetParameter(type, representativeInstance, rule.ParameterName, scope);
                if (p == null)
                {
                    match.Checks.Add($"Param[{scope}]: '{rule.ParameterName}' => NOT FOUND");
                    return RuleEvalOutcome.FailedMissingParameter; // <-- key semantic
                }

                bool ok = CompareParameter(doc, p, rule, out var detail);
                match.Checks.Add($"Param[{scopeUsed}]: {detail} => {(ok ? "OK" : "FAIL")}");

                if (!ok) return RuleEvalOutcome.Failed;

                if (scopeUsed.StartsWith("Instance", StringComparison.OrdinalIgnoreCase) && representativeInstance != null)
                    match.Checks.Add($"InstanceScopeUsed: representativeInstanceId={representativeInstance.Id.Value}");
            }

            return RuleEvalOutcome.Matched;
        }

        private static string GetFamilyName(ElementType type)
        {
            if (type is FamilySymbol fs)
                return fs.FamilyName ?? "";

            try
            {
                var famName = type.FamilyName;
                if (!string.IsNullOrWhiteSpace(famName)) return famName;
            }
            catch { }

            var p = type.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM);
            return p?.AsString() ?? p?.AsValueString() ?? "";
        }

        private static (Parameter? p, string scopeUsed) GetParameter(
            ElementType type,
            Element? instance,
            string name,
            ParamScope scope)
        {
            name = name.Trim();

            if (scope == ParamScope.Type)
                return (type.LookupParameter(name), "Type");

            if (scope == ParamScope.Instance)
                return (instance?.LookupParameter(name), "Instance");

            // Any: instance first, then type
            var pi = instance?.LookupParameter(name);
            if (pi != null) return (pi, "Instance(Any)");

            var pt = type.LookupParameter(name);
            return (pt, "Type(Any)");
        }

        private static bool CompareParameter(Document doc, Parameter p, ClassificationRule rule, out string detail)
        {
            var op = (rule.Operator ?? "Equals").Trim();
            var vt = (rule.ValueType ?? "Text").Trim();

            bool IsTrue(string s) =>
                s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("1");

            if (vt.Equals("Bool", StringComparison.OrdinalIgnoreCase))
            {
                int actual = p.StorageType == StorageType.Integer ? p.AsInteger() : (IsTrue(p.AsValueString() ?? "") ? 1 : 0);
                int expected = IsTrue(rule.ParameterValue1 ?? "") ? 1 : 0;
                detail = $"Bool actual={actual} op={op} expected={expected}";
                return op.Equals("NotEquals", StringComparison.OrdinalIgnoreCase) ? actual != expected : actual == expected;
            }

            if (vt.Equals("Number", StringComparison.OrdinalIgnoreCase) ||
                vt.Equals("Double", StringComparison.OrdinalIgnoreCase) ||
                vt.Equals("Integer", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadNumeric(p, out var actual))
                {
                    detail = $"Number actual=<unreadable> op={op} expected={rule.ParameterValue1}";
                    return false;
                }

                if (!double.TryParse(rule.ParameterValue1 ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out var exp1))
                {
                    detail = $"Number actual={actual} op={op} expected=<bad '{rule.ParameterValue1}'>";
                    return false;
                }

                exp1 = ConvertExpectedToInternalIfPossible(exp1, rule.Unit);

                var tol = rule.Tolerance ?? 1e-6;

                if (op.Equals("Between", StringComparison.OrdinalIgnoreCase))
                {
                    if (!double.TryParse(rule.ParameterValue2 ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out var exp2Raw))
                    {
                        detail = $"Number actual={actual:R} op=Between expected2=<bad '{rule.ParameterValue2}'>";
                        return false;
                    }

                    var exp2 = ConvertExpectedToInternalIfPossible(exp2Raw, rule.Unit);
                    var lo = Math.Min(exp1, exp2) - tol;
                    var hi = Math.Max(exp1, exp2) + tol;

                    detail = $"Number actual={actual:R} op=Between lo={lo:R} hi={hi:R} tol={tol}";
                    return actual >= lo && actual <= hi;
                }

                detail = $"Number actual={actual:R} op={op} expected1={exp1:R} tol={tol}";
                return op.ToLowerInvariant() switch
                {
                    "equals" => Math.Abs(actual - exp1) <= tol,
                    "notequals" => Math.Abs(actual - exp1) > tol,
                    "greaterthan" => actual > exp1 + tol,
                    "greaterthanorequal" => actual >= exp1 - tol,
                    "lessthan" => actual < exp1 - tol,
                    "lessthanorequal" => actual <= exp1 + tol,
                    _ => Math.Abs(actual - exp1) <= tol
                };
            }

            // Text default
            var actualText = ReadTextForComparison(p);
            var expectedText = (rule.ParameterValue1 ?? "").Trim();

            if (op.Equals("TokenContains", StringComparison.OrdinalIgnoreCase))
            {
                var tokens = expectedText.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                bool ok = tokens.Any(t => actualText.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                detail = $"Text actual='{actualText}' op=TokenContains tokens='{expectedText}'";
                return ok;
            }

            if (op.Equals("Regex", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    bool ok = Regex.IsMatch(actualText ?? "", expectedText, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    detail = $"Text actual='{actualText}' op=Regex pattern='{expectedText}'";
                    return ok;
                }
                catch
                {
                    detail = $"Text actual='{actualText}' op=Regex pattern=<invalid>";
                    return false;
                }
            }

            detail = $"Text actual='{actualText}' op={op} expected='{expectedText}'";
            return StringMatch(actualText, expectedText, op);
        }

        private static string ReadTextForComparison(Parameter p)
        {
            if (p.StorageType == StorageType.String)
                return (p.AsString() ?? "").Trim();

            return (p.AsValueString() ?? p.AsString() ?? "").Trim();
        }

        private static bool TryReadNumeric(Parameter p, out double value)
        {
            value = 0;

            switch (p.StorageType)
            {
                case StorageType.Double:
                    value = p.AsDouble();
                    return true;
                case StorageType.Integer:
                    value = p.AsInteger();
                    return true;
                case StorageType.String:
                    return double.TryParse((p.AsString() ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
                default:
                    return false;
            }
        }

        private static double ConvertExpectedToInternalIfPossible(double expected, string unitToken)
        {
            if (string.IsNullOrWhiteSpace(unitToken)) return expected;

            unitToken = unitToken.Trim().ToLowerInvariant();
            return unitToken switch
            {
                "mm" => UnitUtils.ConvertToInternalUnits(expected, UnitTypeId.Millimeters),
                "cm" => UnitUtils.ConvertToInternalUnits(expected, UnitTypeId.Centimeters),
                "m" => UnitUtils.ConvertToInternalUnits(expected, UnitTypeId.Meters),
                "deg" => UnitUtils.ConvertToInternalUnits(expected, UnitTypeId.Degrees),
                _ => expected
            };
        }

        private static bool StringMatch(string input, string target, string mode)
        {
            input ??= "";
            target ??= "";
            mode = (mode ?? "Any").Trim();

            if (IsAny(mode)) return true;

            // ✅ Excel uses this a lot: "AAA|BBB|CCC" means OR tokens
            if (mode.Equals("TokenContains", StringComparison.OrdinalIgnoreCase))
            {
                var tokens = target.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tokens.Length == 0) return true;

                return tokens.Any(t => input.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            // Optional: token equals (rare, but sometimes handy)
            if (mode.Equals("TokenEquals", StringComparison.OrdinalIgnoreCase))
            {
                var tokens = target.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tokens.Length == 0) return true;

                return tokens.Any(t => input.Equals(t, StringComparison.OrdinalIgnoreCase));
            }

            return mode.ToLowerInvariant() switch
            {
                "equals" => input.Equals(target, StringComparison.OrdinalIgnoreCase),
                "notequals" => !input.Equals(target, StringComparison.OrdinalIgnoreCase),
                "contains" => input.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0,
                "startswith" => input.StartsWith(target, StringComparison.OrdinalIgnoreCase),
                "endswith" => input.EndsWith(target, StringComparison.OrdinalIgnoreCase),
                _ => input.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0
            };
        }

        private static bool IsAny(string s) => string.IsNullOrWhiteSpace(s) || s.Equals("Any", StringComparison.OrdinalIgnoreCase);

        private static ParamScope ParseScope(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return ParamScope.Any;
            if (s.Equals("Type", StringComparison.OrdinalIgnoreCase)) return ParamScope.Type;
            if (s.Equals("Instance", StringComparison.OrdinalIgnoreCase)) return ParamScope.Instance;
            return ParamScope.Any;
        }
    }
}
