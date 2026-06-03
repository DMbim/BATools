using System.Text.Json.Serialization;

namespace BATools.ParamCopy.Models
{
    public enum FilterCondition
    {
        Equals,
        NotEquals,
        Contains,
        NotContains,
        GreaterThan,
        LessThan,
        HasValue,
        HasNoValue
    }

    public class FilterRule
    {
        public string ParameterName { get; set; } = string.Empty;
        public FilterCondition Condition { get; set; } = FilterCondition.Equals;
        public string Value { get; set; } = string.Empty;
    }
}
