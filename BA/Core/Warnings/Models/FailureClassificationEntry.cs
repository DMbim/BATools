// FILE: BA_Tools/Warnings/Models/FailureClassificationEntry.cs
using System;

namespace BA.Warnings.Models
{
    public sealed class FailureClassificationEntry
    {
        public Guid Guid { get; set; }
        public string Description { get; set; }
        public int Severity { get; set; }
        public string Category { get; set; }
    }
}