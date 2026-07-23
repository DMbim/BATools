// File: BA/Core/CurveToElement/Models/GenerationResult.cs
// Action: CREATE NEW

using System.Collections.Generic;

namespace BA.Core.CurveToElement.Models
{
    public class GenerationResult
    {
        public bool Success { get; }
        public string Message { get; }
        public int CreatedWallCount { get; }
        public IReadOnlyList<string> Warnings { get; }

        public GenerationResult(bool success, string message, int createdWallCount, IReadOnlyList<string> warnings)
        {
            Success = success;
            Message = message;
            CreatedWallCount = createdWallCount;
            Warnings = warnings ?? new List<string>();
        }
    }
}