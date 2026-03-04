using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BA.UI
{
    public class SharedParamSet
    {
        public string  Name { get; set; }
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
