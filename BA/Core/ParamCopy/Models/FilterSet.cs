using System.Collections.Generic;
using ParamFilterRule = BATools.ParamCopy.Models.FilterRule;

namespace BATools.ParamCopy.Models
{
    public enum FilterSetOperator { And, Or }

    public class FilterSet
    {
        public FilterSetOperator Operator { get; set; } = FilterSetOperator.And;

        /// <summary>
        /// Always initialized with one rule so the XAML binding to Rules[0]
        /// has a valid target immediately.
        /// </summary>
        public List<ParamFilterRule> Rules { get; set; } = new() { new ParamFilterRule() };
        public ParamFilterRule PrimaryRule => Rules[0];
    }
}
