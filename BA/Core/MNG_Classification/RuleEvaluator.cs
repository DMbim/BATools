using Autodesk.Revit.DB;
using System;

namespace BA.Classification
{
    public static class RuleEvaluator
    {
        public static (bool Matched, string Reason) Evaluate(Element e, ClassificationRule rule)
        {
            // 1️⃣ Category
            if (!string.IsNullOrEmpty(rule.RevitCategory) && rule.RevitCategoryMatchMode != "Any")
            {
                if (!StringMatch(e.Category?.Name, rule.RevitCategory, rule.RevitCategoryMatchMode))
                    return (false, "Category mismatch");
            }

            // 2️⃣ Family
            var famName = e.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString() ?? "";
            if (!string.IsNullOrEmpty(rule.FamilyName) && rule.FamilyMatchMode != "Any")
            {
                if (!StringMatch(famName, rule.FamilyName, rule.FamilyMatchMode))
                    return (false, "Family mismatch");
            }

            // 3️⃣ Type
            var type = e.Document.GetElement(e.GetTypeId()) as ElementType;
            var typeName = type?.Name ?? "";
            if (!string.IsNullOrEmpty(rule.TypeName) && rule.TypeMatchMode != "Any")
            {
                if (!StringMatch(typeName, rule.TypeName, rule.TypeMatchMode))
                    return (false, "Type mismatch");
            }

            // 4️⃣ Parameter (optional)
            if (!string.IsNullOrEmpty(rule.ParameterName))
            {
                double? valueNum = null;
                string valueStr = null;

                var param = GetParameter(e, rule.ParameterName, rule.ParameterScope);
                if (param == null) return (false, "Parameter not found");

                if (rule.ValueType?.Equals("Number", StringComparison.OrdinalIgnoreCase) == true)
                    valueNum = param.AsDouble();
                else
                    valueStr = param.AsValueString() ?? "";

                if (!CompareParameter(valueNum, valueStr, rule))
                    return (false, "Parameter condition failed");
            }

            return (true, "Matched all conditions");
        }

        private static bool StringMatch(string input, string target, string mode)
        {
            if (string.IsNullOrEmpty(target)) return true;
            input ??= "";

            return mode?.ToLower() switch
            {
                "equals" => input.Equals(target, StringComparison.OrdinalIgnoreCase),
                "contains" => input.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0,
                "startswith" => input.StartsWith(target, StringComparison.OrdinalIgnoreCase),
                "endswith" => input.EndsWith(target, StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        private static Parameter GetParameter(Element e, string name, string scope)
        {
            Parameter p = e.LookupParameter(name);
            if (p != null) return p;

            if (scope?.Equals("Type", StringComparison.OrdinalIgnoreCase) == true)
            {
                var type = e.Document.GetElement(e.GetTypeId()) as ElementType;
                return type?.LookupParameter(name);
            }

            return null;
        }

        private static bool CompareParameter(double? valueNum, string valueStr, ClassificationRule rule)
        {
            if (rule.ValueType?.Equals("Number", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (!double.TryParse(rule.ParameterValue1, out double val1)) return false;
                double tol = rule.Tolerance ?? 0.001;
                double num = valueNum ?? 0;

                return rule.Operator.ToLower() switch
                {
                    "equals" => Math.Abs(num - val1) <= tol,
                    "greaterthan" => num > val1 + tol,
                    "greaterthanorequal" => num >= val1 - tol,
                    "lessthan" => num < val1 - tol,
                    "lessthanorequal" => num <= val1 + tol,
                    "between" => double.TryParse(rule.ParameterValue2, out double val2)
                        && num >= Math.Min(val1, val2) - tol
                        && num <= Math.Max(val1, val2) + tol,
                    _ => true
                };
            }

            if (rule.ValueType?.Equals("Text", StringComparison.OrdinalIgnoreCase) == true)
                return StringMatch(valueStr, rule.ParameterValue1, rule.Operator);

            return true;
        }
    }
}
