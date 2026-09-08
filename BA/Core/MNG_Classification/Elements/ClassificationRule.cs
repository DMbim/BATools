using System;

namespace BA.Classification
{
    public class ClassificationRule
     {
        public string RuleId { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public int RulePriority { get; set; } = 0;     // Higher wins (DESC)
        public bool StopOnMatch { get; set; } = false; // kept for Excel compatibility

        public string TargetLevelCode { get; set; } = "";
        public string Notes { get; set; } = "";

        public string RevitCategory { get; set; } = "";
        public string RevitCategoryMatchMode { get; set; } = "Equals";

        public string FamilyName { get; set; } = "";
        public string FamilyMatchMode { get; set; } = "Any";
        public string TargetLabel_EN { get; set; } = "";
        public string TargetLabel_CZ { get; set; } = "";

        public string TargetLabel_Local { get; set; } = "";


        public string TypeName { get; set; } = "";
        public string TypeMatchMode { get; set; } = "Any";

        public string ParameterName { get; set; } = "";
        public string ParameterScope { get; set; } = "Any"; // Any / Instance / Type
        public string ValueType { get; set; } = "Text";      // Text / Number / Integer / Bool
        public string Operator { get; set; } = "Equals";

        public string ParameterValue1 { get; set; } = "";
        public string ParameterValue2 { get; set; } = "";
        public double? Tolerance { get; set; } = null;
        public string Unit { get; set; } = "";

        // Derived
        public string Domain { get; private set; } = "";
        public string Group { get; private set; } = "";
        public string Subcode { get; private set; } = "";

        // Determinism helpers
        public int RowOrder { get; set; } = int.MaxValue; // Excel row number (1-based)
        public int SpecificityScore { get; set; } = 0;     // computed after load

        // Category resolution (filled per-document in preprocess)
        public int? ResolvedBuiltInCategoryInt { get; set; } = null; // (int)BuiltInCategory when parsed

        public void ParseCode()
        {
            try
            {
                var parts = TargetLevelCode?.Split('.', '-');
                Domain = parts?.Length > 0 ? parts[0] : "";
                Group = parts?.Length > 1 ? parts[1] : "";
                Subcode = parts?.Length > 2 ? parts[2] : "";
            }
            catch
            {
                Domain = Group = Subcode = "";
            }
        }

        public override string ToString() => $"{RuleId} → {TargetLevelCode}";
    }
}
