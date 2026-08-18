// BA/UI/Parameters/SharedParamSet.cs
using System.Collections.Generic;

namespace BA.UI.Parameters
{
    public class SharedParamSet
    {
        public string Name { get; set; }
        public List<SharedParamItem> Items { get; set; } = new();
    }

    public class SharedParamItem
    {
        public string SharedName { get; set; }
        public string Group { get; set; } = "PG_DATA"; // BuiltInParameterGroup name
        public bool IsInstance { get; set; } = true;
    }

    public class SharedRow
    {
        public string Name { get; set; }
        public string Spec { get; set; }
    }
}