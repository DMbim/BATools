using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace BA.Updates
{
    internal static class VersionUtil
    {
        public static Version GetInstalledVersion(Assembly asm)
        {
            // Prefer InformationalVersion (supports SemVer-like strings)
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (TryParseLoose(info, out var vInfo))
                return vInfo;

            // Fallback: AssemblyName.Version
            var vAsm = asm.GetName().Version;
            return vAsm ?? new Version(0, 0, 0);
        }

        public static bool TryParseLoose(string? s, out Version version)
        {
            version = new Version(0, 0, 0);
            if (string.IsNullOrWhiteSpace(s)) return false;

            // strip leading 'v' and any suffix like "-beta"
            s = s.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(1);

            var core = s.Split('-', '+').FirstOrDefault() ?? s;
            // keep only digits and dots
            core = new string(core.Where(c => char.IsDigit(c) || c == '.').ToArray());

            if (string.IsNullOrWhiteSpace(core)) return false;

            // Version.Parse needs at least "a.b"
            // We accept:
            // 1 -> 1.0
            // 1.2 -> 1.2
            // 1.2.3 -> 1.2.3
            // 1.2.3.4 -> 1.2.3.4
            var parts = core.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) core = $"{parts[0]}.0.0";
            else if (parts.Length == 2) core = $"{parts[0]}.{parts[1]}.0";
            if (Version.TryParse(core, out var v))
            {
                version = v;
                return true;
            }
            return false;
        }

        public static int Compare(Version a, Version b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            return a.CompareTo(b);
        }
    }
}
