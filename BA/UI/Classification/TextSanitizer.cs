using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BA.UI.Classification
{
    public static class TextSanitizer
    {
        public static string SanitizeForRevit(string? s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            // Normalize common Excel/Copy-paste junk
            s = s.Replace('\u00A0', ' ');   // non-breaking space
            s = s.Replace("\u200B", "");    // zero-width space
            s = s.Replace("\uFEFF", "");    // BOM / zero-width no-break

            // Strip control characters (this is the usual culprit)
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (char.IsControl(ch))
                    continue; // removes tabs, CR/LF, and nasty hidden chars like 0x15
                sb.Append(ch);
            }

            // Optional: collapse weird multiple spaces? keep it simple:
            return sb.ToString().Trim();
        }
    }
}