// File: BA/Core/CurveToElement/Models/GenerationResult.cs
// Action: REPLACE (full file)

using System.Collections.Generic;

namespace BA.Core.CurveToElement.Models
{
    public class GenerationResult
    {
        public bool Success { get; }
        public string Message { get; }
        public int CreatedWallCount { get; }
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// Count of source detail line elements deleted as a result of the "delete lines
        /// after creation" option. This counts only the source lines themselves, the ids
        /// that were actually handed to Document.Delete, not any dependent elements Revit
        /// cascade deleted along with them (dimensions, tags, etc. referencing those lines).
        /// </summary>
        public int DeletedLineCount { get; }

        public GenerationResult(bool success, string message, int createdWallCount, IReadOnlyList<string> warnings, int deletedLineCount)
        {
            Success = success;
            Message = message;
            CreatedWallCount = createdWallCount;
            Warnings = warnings ?? new List<string>();
            DeletedLineCount = deletedLineCount;
        }
    }
}
