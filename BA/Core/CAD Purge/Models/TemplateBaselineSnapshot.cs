// File: BA_Tools/CadPurge/Models/TemplateBaselineSnapshot.cs
using System;
using System.Collections.Generic;

namespace BA.CadPurge.Models
{
    /// <summary>
    /// Snapshot of every LinePatternElement / TextNoteType name found in the reference standards
    /// template (BA_RevitTemplate_v26.rte). Used by PurgeScanService to distinguish genuine
    /// DWG-imported junk from un-renamed stock Revit content that happens to lack the BA_ prefix
    /// but isn't actually a purge target.
    /// </summary>
    public sealed class TemplateBaselineSnapshot
    {
        public IReadOnlySet<string> LinePatternNames { get; }
        public IReadOnlySet<string> TextStyleNames { get; }

        public TemplateBaselineSnapshot(IReadOnlySet<string> linePatternNames, IReadOnlySet<string> textStyleNames)
        {
            LinePatternNames = linePatternNames ?? throw new ArgumentNullException(nameof(linePatternNames));
            TextStyleNames = textStyleNames ?? throw new ArgumentNullException(nameof(textStyleNames));
        }

        public bool Contains(PurgeItemType itemType, string name)
        {
            return itemType switch
            {
                PurgeItemType.LinePattern => LinePatternNames.Contains(name),
                PurgeItemType.TextStyle => TextStyleNames.Contains(name),
                _ => false
            };
        }
    }
}