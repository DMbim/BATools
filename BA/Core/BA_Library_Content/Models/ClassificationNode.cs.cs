using System;
using System.Collections.ObjectModel;

namespace BA.Core.Content.Models
{
    public sealed class ClassificationNode
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ParentCode { get; set; } = string.Empty;

        public string Level { get; set; } = string.Empty;

        public ObservableCollection<ClassificationNode> Children { get; } = new();

        public string DisplayText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Name))
                    return Code;

                return $"{Code}  {Name}";
            }
        }
    }
}