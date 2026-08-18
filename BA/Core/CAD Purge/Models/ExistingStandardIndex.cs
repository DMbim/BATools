// File: BA_Tools/CadPurge/Models/ExistingStandardIndex.cs
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BA.CadPurge.Models
{
    /// <summary>
    /// Case-insensitive Name -&gt; ElementId index of every LinePatternElement and TextNoteType
    /// already present in the active document. Build ONCE per batch via
    /// CorporateStandardResolverService.BuildExistingIndex and reuse across every candidate being
    /// resolved — re-collecting the document per candidate would turn an N-candidate batch into
    /// an O(N) full-document scan for each one.
    /// </summary>
    public sealed class ExistingStandardIndex
    {
        public Dictionary<string, ElementId> LinePatterns { get; }
        public Dictionary<string, ElementId> TextStyles { get; }

        public ExistingStandardIndex(Dictionary<string, ElementId> linePatterns, Dictionary<string, ElementId> textStyles)
        {
            LinePatterns = linePatterns ?? throw new ArgumentNullException(nameof(linePatterns));
            TextStyles = textStyles ?? throw new ArgumentNullException(nameof(textStyles));
        }
    }
}