using System;
using System.Collections.Generic;

namespace BATools.SelectionManager.Models
{
    public class FamilyFavGroupDefinition
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Group";
        public bool IsExpanded { get; set; } = true;
        public List<FamilyTypeEntry> Entries { get; set; } = new();
    }
}