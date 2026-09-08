// FILE: BA_Tools/Warnings/Models/DuplicateInstanceGroup.cs
using System.Collections.Generic;

namespace BA.Warnings.Models
{
    public sealed class DuplicateInstanceGroup
    {
        public WarningItem SourceWarning { get; set; }
        public List<DuplicateInstanceCandidate> Candidates { get; set; } = new List<DuplicateInstanceCandidate>();
    }
}