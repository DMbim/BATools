// File: BA_Tools/CadPurge/Models/MappingConfig.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.CadPurge.Models
{
    /// <summary>
    /// Deserialized contents of corporate_standards.json — the full corporate-standard mapping
    /// definition for CAD Purge. Loaded and validated by MappingConfigService; never constructed
    /// directly by UI or scan code.
    /// </summary>
    public sealed class MappingConfig
    {
        /// <summary>
        /// Absolute path to the reference standards template (e.g. BA_RevitTemplate_v26.rte)
        /// used to resolve mapping targets that don't yet exist in the active document.
        /// </summary>
        public string TemplateFilePath { get; set; }

        /// <summary>Prefix that marks an element as already corporate-standard (default "BA_").</summary>
        public string StandardPrefix { get; set; } = "BA_";

        public List<MappingRule> Rules { get; set; } = new();

        /// <summary>
        /// Validates the loaded config. Returns a list of human-readable problems; an empty list
        /// means the config is safe to use. Never throws — callers decide how to surface errors.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(TemplateFilePath))
                problems.Add("templateFilePath is missing or empty.");

            if (string.IsNullOrWhiteSpace(StandardPrefix))
                problems.Add("standardPrefix is missing or empty.");

            if (Rules == null || Rules.Count == 0)
            {
                problems.Add("rules array is empty — no mapping targets are defined.");
                return problems;
            }

            for (int i = 0; i < Rules.Count; i++)
            {
                MappingRule rule = Rules[i];

                if (rule == null)
                {
                    problems.Add($"rules[{i}] is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(rule.SourceNamePattern))
                    problems.Add($"rules[{i}] (ItemType={rule.ItemType}) has an empty sourceNamePattern.");

                if (string.IsNullOrWhiteSpace(rule.TargetName))
                    problems.Add($"rules[{i}] (ItemType={rule.ItemType}) has an empty targetName.");

                if (!string.IsNullOrWhiteSpace(rule.TargetName)
                    && !string.IsNullOrWhiteSpace(StandardPrefix)
                    && !rule.TargetName.StartsWith(StandardPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add(
                        $"rules[{i}] targetName '{rule.TargetName}' does not start with the standard prefix " +
                        $"'{StandardPrefix}' — likely a typo in corporate_standards.json.");
                }

                if (rule.IsRegex)
                {
                    try { _ = rule.IsMatch("__validation_probe__"); }
                    catch (Exception ex)
                    {
                        problems.Add($"rules[{i}] sourceNamePattern is not a valid regex: {ex.Message}");
                    }
                }
            }

            IEnumerable<string> duplicateTargets = Rules
                .Where(r => r != null)
                .GroupBy(r => (r.ItemType, Pattern: r.SourceNamePattern?.Trim().ToUpperInvariant()))
                .Where(g => g.Count() > 1)
                .Select(g => $"ItemType={g.Key.ItemType}, sourceNamePattern='{g.Key.Pattern}'");

            foreach (string dup in duplicateTargets)
                problems.Add($"Duplicate rule detected ({dup}) — only the first match will ever be used.");

            return problems;
        }
    }
}