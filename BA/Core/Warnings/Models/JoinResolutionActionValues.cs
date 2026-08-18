// FILE: BA_Tools/Warnings/Models/JoinResolutionActionValues.cs
using System;

namespace BA.Warnings.Models
{
    public static class JoinResolutionActionValues
    {
        public static Array All => Enum.GetValues(typeof(JoinResolutionAction));
    }
}