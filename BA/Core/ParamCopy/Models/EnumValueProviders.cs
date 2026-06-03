using System;
using System.Collections.Generic;
using System.Linq;

namespace BATools.ParamCopy.Models
{
    public static class FilterConditionValues
    {
        public static IReadOnlyList<FilterCondition> All { get; }
            = Enum.GetValues<FilterCondition>().ToList();
    }

    public static class FilterSetOperatorValues
    {
        public static IReadOnlyList<FilterSetOperator> All { get; }
            = Enum.GetValues<FilterSetOperator>().ToList();
    }
}
