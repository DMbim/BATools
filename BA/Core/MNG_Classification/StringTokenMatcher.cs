using System;
using System.Linq;

namespace BA.Core.Classification
{
    public static class StringTokenMatcher
    {
        /// <summary>
        /// OR logic:
        /// - Empty tokens string => match
        /// - Tokens separated by '|'
        /// - Returns true if any token is contained in haystack (case-insensitive)
        /// </summary>
        public static bool MatchesOrTokens(string haystack, string tokensOr)
        {
            if (string.IsNullOrWhiteSpace(tokensOr))
                return true;

            haystack ??= string.Empty;

            var tokens = tokensOr
                .Split('|')
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            if (tokens.Count == 0)
                return true;

            return tokens.Any(t =>
                haystack.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
