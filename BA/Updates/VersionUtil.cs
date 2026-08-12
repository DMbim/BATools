using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BA.Updates
{
    internal static class VersionUtil
    {
        private const string VersionFileName = "BATools.version";

        /// <summary>
        /// Authoritative source is BATools.version, deployed next to BA.dll by the installer
        /// and written by Publish.ps1 from the same VersionPrefix used for the assembly and
        /// the GitHub release tag. Falls back to AssemblyInformationalVersion, then
        /// AssemblyName.Version, only if the file is missing, which happens for a debug
        /// build run straight from Visual Studio without going through the installer.
        /// </summary>
        public static Version GetInstalledVersion(Assembly asm)
        {
            var fromFile = TryReadVersionFile(asm);
            if (fromFile != null)
                return fromFile;

            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (TryParseLoose(info, out var vInfo))
                return vInfo;

            var vAsm = asm.GetName().Version;
            return vAsm ?? new Version(0, 0, 0);
        }

        private static Version? TryReadVersionFile(Assembly asm)
        {
            try
            {
                var dir = Path.GetDirectoryName(asm.Location);
                if (string.IsNullOrWhiteSpace(dir))
                    return null;

                var path = Path.Combine(dir, VersionFileName);
                if (!File.Exists(path))
                    return null;

                var text = File.ReadAllText(path);
                return TryParseLoose(text, out var v) ? v : null;
            }
            catch
            {
                // Never let a missing or locked version file break a caller that just wants
                // to know what version is installed.
                return null;
            }
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