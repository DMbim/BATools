// FILE: BA_Tools/Warnings/Models/DuplicateInstanceCandidate.cs
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BA.Warnings.Models
{
    public sealed class DuplicateInstanceCandidate
    {
        public ElementId ElementId { get; set; }
        public string Category { get; set; }
        public string FamilyAndType { get; set; }

        // Approximate only: lowest Element Id within its duplicate group, not
        // a guaranteed creation timestamp, Revit doesn't expose true creation
        // time via the API. Shown as a hint, not a fact.
        public bool IsLikelyOlder { get; set; }

        public int FilledParameterCount { get; set; }
        public List<KeyValuePair<string, string>> ParameterSnapshot { get; set; } = new List<KeyValuePair<string, string>>();

        public bool MarkedForDeletion { get; set; }
    }
}