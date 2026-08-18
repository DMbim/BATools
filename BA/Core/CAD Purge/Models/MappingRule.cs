// File: BA_Tools/CadPurge/Models/MappingRule.cs
using System;
using System.Text.RegularExpressions;

namespace BA.CadPurge.Models
{
    /// <summary>
    /// One row of corporate_standards.json: matches source element names against a pattern and
    /// specifies which corporate-standard element name they should be mapped to.
    /// </summary>
    public sealed class MappingRule
    {
        public PurgeItemType ItemType { get; set; }

        /// <summary>
        /// Wildcard pattern (default) using * (any run of characters) and ? (single character),
        /// matched case-insensitively against the candidate's Name. Set IsRegex = true to use
        /// SourceNamePattern as a raw .NET regular expression instead.
        /// </summary>
        public string SourceNamePattern { get; set; }

        public bool IsRegex { get; set; }

        /// <summary>Name of the corporate-standard LinePatternElement/TextNoteType this rule maps to.</summary>
        public string TargetName { get; set; }

        private Regex _compiled;

        /// <summary>
        /// True if candidateName matches this rule's SourceNamePattern. The compiled Regex is
        /// cached on first use — safe to call repeatedly across a full document scan.
        /// </summary>
        public bool IsMatch(string candidateName)
        {
            if (string.IsNullOrEmpty(candidateName))
                return false;

            if (_compiled == null)
            {
                if (string.IsNullOrWhiteSpace(SourceNamePattern))
                    throw new InvalidOperationException(
                        $"MappingRule for ItemType '{ItemType}' has an empty SourceNamePattern and cannot be matched.");

                string pattern = IsRegex ? SourceNamePattern : WildcardToRegexPattern(SourceNamePattern);
                _compiled = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
            }

            return _compiled.IsMatch(candidateName);
        }

        private static string WildcardToRegexPattern(string wildcard)
        {
            string escaped = Regex.Escape(wildcard);
            escaped = escaped.Replace("\\*", ".*").Replace("\\?", ".");
            return "^" + escaped + "$";
        }
    }
}