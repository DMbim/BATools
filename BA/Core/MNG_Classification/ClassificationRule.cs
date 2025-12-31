using System;

namespace BA.Classification
{
    public class ClassificationRule
    {
        public string RuleId { get; set; }
        public bool Enabled { get; set; }
        public int RulePriority { get; set; }
        public bool StopOnMatch { get; set; }

        public string TargetLevelCode { get; set; }
        public string TargetLabel_EN { get; set; }
        public string TargetLabel_Local { get; set; }

        public string RevitCategory { get; set; }
        public string RevitCategoryMatchMode { get; set; }

        public string FamilyName { get; set; }
        public string FamilyMatchMode { get; set; }

        public string TypeName { get; set; }
        public string TypeMatchMode { get; set; }

        public string ParameterName { get; set; }
        public string ParameterScope { get; set; }
        public string ValueType { get; set; }
        public string Operator { get; set; }

        public string ParameterValue1 { get; set; }
        public string ParameterValue2 { get; set; }

        public double? Tolerance { get; set; }
        public string Unit { get; set; }

        public string Notes { get; set; }

        // Derived fields
        public string Domain { get; private set; }
        public string Group { get; private set; }
        public string Subcode { get; private set; }

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

        public override string ToString() => $"{RuleId} → {TargetLevelCode} ({TargetLabel_EN})";
    }
}
