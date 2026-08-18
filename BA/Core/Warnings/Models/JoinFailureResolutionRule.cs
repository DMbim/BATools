// FILE: BA_Tools/Warnings/Models/JoinFailureResolutionRule.cs
using System;

namespace BA.Warnings.Models
{
    public enum JoinResolutionAction
    {
        Ignore = 0,
        Join = 1,
        Unjoin = 2
    }

    public sealed class JoinFailureResolutionRule
    {
        public Guid FailureDefinitionGuid { get; set; }
        public string DisplayName { get; set; }
        public JoinResolutionAction Action { get; set; } = JoinResolutionAction.Ignore;
    }
}