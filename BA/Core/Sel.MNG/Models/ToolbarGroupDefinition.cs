using System;
using System.Collections.Generic;

namespace BATools.SelectionManager.Models
{
    public class ToolbarGroupDefinition
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Group";
        public bool IsExpanded { get; set; } = true;
        public List<string> ActionIds { get; set; } = new();
    }
}