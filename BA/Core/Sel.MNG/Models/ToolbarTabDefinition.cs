using System;
using System.Collections.Generic;

namespace BA.SelectionManager.Models
{
    public class ToolbarTabDefinition
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Tab";
        public List<string> PinnedActionIds { get; set; } = new();
        public List<ToolbarGroupDefinition> Groups { get; set; } = new();
    }
} 