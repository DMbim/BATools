using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BA.Core
{
    public static class NameMatcher
    {
        // Prefixes to drop from shared names
        private static readonly string[] Prefixes = { "BA_", "BA-", "BA " };

        public static string Normalize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            // Lowercase
            name = name.Trim().ToLowerInvariant();

            // Split camelCase → space
            name = Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");

            // Replace delimiters with space
            name = name.Replace("_", " ").Replace("-", " ");

            // Remove extra punctuation
            name = Regex.Replace(name, @"[^a-z0-9\s]", " ");

            // Collapse spaces
            name = Regex.Replace(name, @"\s+", " ").Trim();

            // Drop standard prefixes (BA_)
            foreach (var p in Prefixes)
            {
                var pl = p.ToLowerInvariant();
                if (name.StartsWith(pl))
                {
                    name = name.Substring(pl.Length).Trim();
                    break;
                }
            }

            return name;
        }

        public static string[] Tokens(string name)
        {
            var n = Normalize(name);
            return n.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static string TokenKey(string name)
        {
            var tokens = Tokens(name);
            Array.Sort(tokens, StringComparer.Ordinal);
            return string.Join("|", tokens);
        }

        // Levenshtein distance for minor typos
        public static int EditDistance(string a, string b)
        {
            if (a == b) return 0;
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;

            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }

            return d[a.Length, b.Length];
        }

        // Score two token arrays: Jaccard + typo forgiveness on near tokens
        public static double ScoreTokens(string[] a, string[] b)
        {
            if (a.Length == 0 || b.Length == 0) return 0;

            // Map each token in a to best token in b with distance allowance
            var used = new bool[b.Length];
            int hits = 0;

            for (int i = 0; i < a.Length; i++)
            {
                int bestIdx = -1;
                int bestDist = int.MaxValue;
                for (int j = 0; j < b.Length; j++)
                {
                    if (used[j]) continue;
                    int dist = EditDistance(a[i], b[j]);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIdx = j;
                    }
                }

                // Allow small typo: 1 for <=6 chars, 2 for >6 chars
                int thresh = a[i].Length <= 6 ? 1 : 2;
                if (bestIdx >= 0 && (bestDist == 0 || bestDist <= thresh))
                {
                    used[bestIdx] = true;
                    hits++;
                }
            }

            // Jaccard-like score
            int union = a.Length + b.Length - hits;
            return union == 0 ? 0 : (double)hits / union;
        }
    }
}
