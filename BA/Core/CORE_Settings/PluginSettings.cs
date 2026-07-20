using System;
using System.Collections.Generic;

namespace BA.Core.Settings
{
    public sealed class PluginSettings
    {
        // Key -> bool
        public Dictionary<string, bool> Toggles { get; set; } =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // Key -> double (window positions, numeric field values)
        public Dictionary<string, double> Doubles { get; set; } =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        // Key -> string (enum names, composite values like "FamilyName:TypeName")
        public Dictionary<string, string> Strings { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ---- bool ----

        public bool GetBool(string key, bool @default)
        {
            if (string.IsNullOrWhiteSpace(key)) return @default;
            return Toggles.TryGetValue(key, out var v) ? v : @default;
        }

        public void SetBool(string key, bool value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            Toggles[key] = value;
        }

        // ---- double ----

        public double GetDouble(string key, double @default)
        {
            if (string.IsNullOrWhiteSpace(key)) return @default;
            return Doubles.TryGetValue(key, out var v) ? v : @default;
        }

        public void SetDouble(string key, double value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            Doubles[key] = value;
        }

        // ---- string ----

        public string GetString(string key, string @default = "")
        {
            if (string.IsNullOrWhiteSpace(key)) return @default;
            return Strings.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : @default;
        }

        public void SetString(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            Strings[key] = value ?? "";
        }
    }

    /// <summary>
    /// A "binding" between a UI checkbox and a real runtime feature (guard).
    /// BA.App creates these, BA.UI consumes them.
    /// </summary>
    public sealed class ToggleBinding
    {
        public string Key { get; }
        public string Group { get; }
        public string Name { get; }
        public string Description { get; }
        public bool DefaultValue { get; }
        public Func<bool> Getter { get; }
        public Action<bool> Setter { get; }

        public ToggleBinding(
            string key,
            string group,
            string name,
            string description,
            bool defaultValue,
            Func<bool> getter,
            Action<bool> setter)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Group = group ?? "General";
            Name = name ?? key;
            Description = description ?? "";
            DefaultValue = defaultValue;
            Getter = getter ?? throw new ArgumentNullException(nameof(getter));
            Setter = setter ?? throw new ArgumentNullException(nameof(setter));
        }
    }
}
