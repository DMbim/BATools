// File: BA.Core/Selection/SuperSelectorModels.cs
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BA.Core.Selection
{
    public enum SuperSelectorLogic
    {
        And,
        Or
    }

    public enum SuperSelectorModifier
    {
        HasValue,
        NoValue,
        Equals,
        NotEquals,
        Contains,
        NotContains,
        StartsWith,
        EndsWith,
        GreaterThan,
        LessThan,
        GreaterThanOrEqual,
        LessThanOrEqual,
        Between,
        IsOneOf,
        MatchesRegex
    }

    // Pure data. Built by the ViewModel from a SuperSelectorFilterRow and
    // handed to the evaluator/pick filter, which live in Core and never
    // touch UIDocument.Selection. ThresholdA/ThresholdB/CompiledRegex are
    // evaluation-time caches, resolved lazily against the first Parameter
    // instance encountered, so unit conversion and regex compilation happen
    // once per pick session rather than once per candidate element.
    public sealed class SuperSelectorCriterion
    {
        public ElementId ParameterId { get; set; }
        public string ParameterName { get; set; }
        public StorageType StorageType { get; set; }
        public bool IsInstance { get; set; }
        public SuperSelectorModifier Modifier { get; set; }
        public string ValueA { get; set; }
        public string ValueB { get; set; }
        public List<string> ValueList { get; set; }

        internal bool ThresholdsResolved;
        internal double? ThresholdA;
        internal double? ThresholdB;
        internal Regex CompiledRegex;
    }

    public static class SuperSelectorCriteriaEvaluator
    {
        public static bool Evaluate(Document doc, Element element, IReadOnlyList<SuperSelectorCriterion> criteria, SuperSelectorLogic logic)
        {
            if (criteria == null || criteria.Count == 0) return true;

            if (logic == SuperSelectorLogic.And)
            {
                foreach (var c in criteria)
                    if (!EvaluateSingle(doc, element, c)) return false;
                return true;
            }

            foreach (var c in criteria)
                if (EvaluateSingle(doc, element, c)) return true;
            return false;
        }

        private static bool EvaluateSingle(Document doc, Element element, SuperSelectorCriterion c)
        {
            Element target = element;

            if (!c.IsInstance)
            {
                var typeId = element.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId) return false;
                target = doc.GetElement(typeId);
                if (target == null) return false;
            }

            var p = FindParameterById(target, c.ParameterId);

            if (c.Modifier == SuperSelectorModifier.NoValue)
                return p == null || !p.HasValue || IsEffectivelyEmptyString(p);

            if (p == null || !p.HasValue)
                return false;

            if (c.Modifier == SuperSelectorModifier.HasValue)
                return !IsEffectivelyEmptyString(p);

            switch (c.Modifier)
            {
                case SuperSelectorModifier.Equals:
                case SuperSelectorModifier.NotEquals:
                case SuperSelectorModifier.Contains:
                case SuperSelectorModifier.NotContains:
                case SuperSelectorModifier.StartsWith:
                case SuperSelectorModifier.EndsWith:
                case SuperSelectorModifier.IsOneOf:
                case SuperSelectorModifier.MatchesRegex:
                    return EvaluateStringModifier(p, c);

                case SuperSelectorModifier.GreaterThan:
                case SuperSelectorModifier.LessThan:
                case SuperSelectorModifier.GreaterThanOrEqual:
                case SuperSelectorModifier.LessThanOrEqual:
                case SuperSelectorModifier.Between:
                    return EvaluateNumericModifier(p, c);

                default:
                    return false;
            }
        }

        // A shared/project parameter that has a value set to an empty string
        // is treated as "no value" for HasValue/NoValue purposes. Only
        // applies to String storage; a Double parameter set to 0.0 still
        // counts as having a value.
        private static bool IsEffectivelyEmptyString(Parameter p)
            => p.StorageType == StorageType.String && string.IsNullOrWhiteSpace(p.AsString());

        private static bool EvaluateStringModifier(Parameter p, SuperSelectorCriterion c)
        {
            string display = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
            if (display == null)
                return c.Modifier == SuperSelectorModifier.NotEquals || c.Modifier == SuperSelectorModifier.NotContains;

            switch (c.Modifier)
            {
                case SuperSelectorModifier.Equals:
                    return string.Equals(display, c.ValueA, StringComparison.OrdinalIgnoreCase);
                case SuperSelectorModifier.NotEquals:
                    return !string.Equals(display, c.ValueA, StringComparison.OrdinalIgnoreCase);
                case SuperSelectorModifier.Contains:
                    return display.IndexOf(c.ValueA ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
                case SuperSelectorModifier.NotContains:
                    return display.IndexOf(c.ValueA ?? string.Empty, StringComparison.OrdinalIgnoreCase) < 0;
                case SuperSelectorModifier.StartsWith:
                    return display.StartsWith(c.ValueA ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                case SuperSelectorModifier.EndsWith:
                    return display.EndsWith(c.ValueA ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                case SuperSelectorModifier.IsOneOf:
                    return c.ValueList != null && c.ValueList.Any(v => string.Equals(display, v, StringComparison.OrdinalIgnoreCase));
                case SuperSelectorModifier.MatchesRegex:
                    if (string.IsNullOrEmpty(c.ValueA)) return false;
                    try
                    {
                        c.CompiledRegex ??= new Regex(c.ValueA, RegexOptions.IgnoreCase);
                        return c.CompiledRegex.IsMatch(display);
                    }
                    catch (ArgumentException)
                    {
                        // Invalid pattern typed by the user. Never matches
                        // rather than throwing mid-pick and aborting the tool.
                        return false;
                    }
                default:
                    return false;
            }
        }

        private static bool EvaluateNumericModifier(Parameter p, SuperSelectorCriterion c)
        {
            if (p.StorageType != StorageType.Double && p.StorageType != StorageType.Integer)
                return false;

            double numeric = p.StorageType == StorageType.Double ? p.AsDouble() : p.AsInteger();

            if (!c.ThresholdsResolved)
                ResolveThresholds(p, c);

            switch (c.Modifier)
            {
                case SuperSelectorModifier.GreaterThan:
                    return c.ThresholdA.HasValue && numeric > c.ThresholdA.Value;
                case SuperSelectorModifier.LessThan:
                    return c.ThresholdA.HasValue && numeric < c.ThresholdA.Value;
                case SuperSelectorModifier.GreaterThanOrEqual:
                    return c.ThresholdA.HasValue && numeric >= c.ThresholdA.Value;
                case SuperSelectorModifier.LessThanOrEqual:
                    return c.ThresholdA.HasValue && numeric <= c.ThresholdA.Value;
                case SuperSelectorModifier.Between:
                    return c.ThresholdA.HasValue && c.ThresholdB.HasValue &&
                           numeric >= Math.Min(c.ThresholdA.Value, c.ThresholdB.Value) &&
                           numeric <= Math.Max(c.ThresholdA.Value, c.ThresholdB.Value);
                default:
                    return false;
            }
        }

        private static void ResolveThresholds(Parameter p, SuperSelectorCriterion c)
        {
            c.ThresholdsResolved = true;

            if (p.StorageType == StorageType.Integer)
            {
                if (int.TryParse(c.ValueA, out var a)) c.ThresholdA = a;
                if (int.TryParse(c.ValueB, out var b)) c.ThresholdB = b;
                return;
            }

            // Double: unit-aware. The same bound parameter reports a
            // consistent unit spec across every element it appears on, so
            // resolving it once against the first Parameter we see is valid
            // for the rest of the pick session.
            ForgeTypeId unitId = null;
            try { unitId = p.GetUnitTypeId(); }
            catch { /* unitless double, e.g. a ratio or count-like value */ }

            if (double.TryParse(c.ValueA, out var da))
                c.ThresholdA = unitId != null ? UnitUtils.ConvertToInternalUnits(da, unitId) : da;

            if (double.TryParse(c.ValueB, out var db))
                c.ThresholdB = unitId != null ? UnitUtils.ConvertToInternalUnits(db, unitId) : db;
        }

        private static Parameter FindParameterById(Element element, ElementId paramId)
        {
            foreach (Parameter p in element.Parameters)
            {
                if (p.Id == paramId) return p;
            }
            return null;
        }
    }
}