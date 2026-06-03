using System;
using System.Collections.Generic;

namespace BATools.SelectionManager.Models
{
    public class SelectionSet
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public List<string> UniqueIds { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public int ColorArgb { get; set; } = unchecked((int)0xFF4A90D9);
        public DateTime Created { get; set; } = DateTime.UtcNow;
        public DateTime Modified { get; set; } = DateTime.UtcNow;
        public string DocumentFingerprint { get; set; } = string.Empty;
        public SetHealthStatus HealthStatus { get; set; } = SetHealthStatus.Unknown;
        public int StaleCount { get; set; } = 0;

        public SelectionSet Clone()
        {
            return new SelectionSet
            {
                Id = Guid.NewGuid(),
                Name = Name + " (Copy)",
                UniqueIds = new List<string>(UniqueIds),
                Tags = new List<string>(Tags),
                ColorArgb = ColorArgb,
                Created = DateTime.UtcNow,
                Modified = DateTime.UtcNow,
                DocumentFingerprint = DocumentFingerprint,
                HealthStatus = HealthStatus,
                StaleCount = StaleCount
            };
        }
    }
}