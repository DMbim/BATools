// File: BATools-Installer/InstallerArgs.cs
using System;

namespace BATools_Installer
{
    public enum InstallerMode
    {
        Install,
        Update,
        Uninstall
    }

    public sealed class InstallerArgs
    {
        public InstallerMode Mode { get; set; } = InstallerMode.Install;

        // Keep as int: 2026, 2025...
        public int RevitYear { get; set; } = 2026;

        // Example: BA_R26.zip
        public string AssetName { get; set; } = "BA_R26.zip";

        // Optional: BA-side can pass direct asset URL to avoid “latest changed” issues
        public string? AssetUrl { get; set; }

        // Optional: the release tag (e.g. "v1.4.0") that BA resolved when it decided to
        // launch this update. Not used to fetch anything, logging/diagnostics only.
        public string? Tag { get; set; }

        // If > 0, installer waits for that process to exit (Revit PID)
        public int WaitPid { get; set; } = 0;

        // Silent mode for “update after close”
        public bool Silent { get; set; } = false;

        public static InstallerArgs Parse(string[] args)
        {
            var a = new InstallerArgs();

            for (int i = 0; i < args.Length; i++)
            {
                var s = (args[i] ?? "").Trim();

                string Next() => (i + 1 < args.Length) ? (args[++i] ?? "") : "";

                switch (s.ToLowerInvariant())
                {
                    // Support BOTH styles:
                    //   --update / --install / --uninstall
                    // and
                    //   --mode update
                    case "--mode":
                        {
                            var m = Next().Trim().ToLowerInvariant();
                            if (m == "install") a.Mode = InstallerMode.Install;
                            else if (m == "update") a.Mode = InstallerMode.Update;
                            else if (m == "uninstall") a.Mode = InstallerMode.Uninstall;
                            break;
                        }

                    case "--install":
                        a.Mode = InstallerMode.Install;
                        break;

                    case "--update":
                        a.Mode = InstallerMode.Update;
                        break;

                    case "--uninstall":
                        a.Mode = InstallerMode.Uninstall;
                        break;

                    case "--revit":
                    case "--revityear":
                        {
                            var raw = Next();
                            if (int.TryParse(raw, out var y) && y > 2000) a.RevitYear = y;
                            break;
                        }

                    case "--asset":
                        {
                            var asset = Next();
                            if (!string.IsNullOrWhiteSpace(asset)) a.AssetName = asset.Trim();
                            break;
                        }

                    case "--asseturl":
                        {
                            var url = Next();
                            if (!string.IsNullOrWhiteSpace(url)) a.AssetUrl = url.Trim().Trim('"');
                            break;
                        }

                    case "--tag":
                        {
                            var tag = Next();
                            if (!string.IsNullOrWhiteSpace(tag)) a.Tag = tag.Trim().Trim('"');
                            break;
                        }

                    case "--waitpid":
                        {
                            var raw = Next();
                            if (int.TryParse(raw, out var pid) && pid > 0) a.WaitPid = pid;
                            break;
                        }

                    case "--silent":
                        a.Silent = true;
                        break;
                }
            }

            return a;
        }
    }
}