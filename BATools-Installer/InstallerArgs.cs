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

    /// <summary>How this installer process was started. Decides which buttons the window shows.</summary>
    public enum LaunchSource
    {
        /// <summary>Opened by double clicking the exe. Year picker visible, Install allowed.</summary>
        Direct,

        /// <summary>Opened from the Revit ribbon. Locked to that Revit year.</summary>
        Ribbon,

        /// <summary>Opened by the hidden waiter after Revit closed because a newer version exists.</summary>
        AfterClose
    }

    public sealed class InstallerArgs
    {
        public InstallerMode Mode { get; set; } = InstallerMode.Install;

        // Keep as int: 2026, 2025...
        public int RevitYear { get; set; } = 2026;

        // Example: BA_R26.zip
        public string AssetName { get; set; } = "BA_R26.zip";

        // Optional: direct asset URL, avoids "latest changed" issues.
        public string? AssetUrl { get; set; }

        // Optional: release tag such as "v1.6.8". For the AfterClose window it is the latest
        // version the waiter already found, so the window does not call GitHub a second time.
        public string? Tag { get; set; }

        // If > 0, wait for that process to exit before doing anything (legacy and waiter use).
        public int WaitPid { get; set; } = 0;

        // Revit process that opened the ribbon window. Always included when closing Revit.
        public int RevitPid { get; set; } = 0;

        // Legacy headless mode, still supported for scripts and older BA.dll versions.
        public bool Silent { get; set; } = false;

        // Hidden waiter: wait for WaitPid to exit, check GitHub, open a window only if newer.
        public bool WaitAndCheck { get; set; } = false;

        public LaunchSource Source { get; set; } = LaunchSource.Direct;

        // Set on the copy that was started from the temp folder. Prevents relaunch loops.
        public bool Relaunched { get; set; } = false;

        // True when --mode/--install/--update/--uninstall was given. A Direct launch with an
        // explicit mode runs immediately (legacy behavior used by older BA.dll versions).
        public bool HasExplicitMode { get; set; } = false;

        public static InstallerArgs Parse(string[] args)
        {
            var a = new InstallerArgs();

            for (int i = 0; i < args.Length; i++)
            {
                var s = (args[i] ?? "").Trim();

                string Next() => (i + 1 < args.Length) ? (args[++i] ?? "") : "";

                switch (s.ToLowerInvariant())
                {
                    case "--mode":
                        {
                            var m = Next().Trim().ToLowerInvariant();
                            if (m == "install") { a.Mode = InstallerMode.Install; a.HasExplicitMode = true; }
                            else if (m == "update") { a.Mode = InstallerMode.Update; a.HasExplicitMode = true; }
                            else if (m == "uninstall") { a.Mode = InstallerMode.Uninstall; a.HasExplicitMode = true; }
                            break;
                        }

                    case "--install":
                        a.Mode = InstallerMode.Install;
                        a.HasExplicitMode = true;
                        break;

                    case "--update":
                        a.Mode = InstallerMode.Update;
                        a.HasExplicitMode = true;
                        break;

                    case "--uninstall":
                        a.Mode = InstallerMode.Uninstall;
                        a.HasExplicitMode = true;
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

                    case "--revitpid":
                        {
                            var raw = Next();
                            if (int.TryParse(raw, out var pid) && pid > 0) a.RevitPid = pid;
                            break;
                        }

                    case "--silent":
                        a.Silent = true;
                        break;

                    case "--waitandcheck":
                        a.WaitAndCheck = true;
                        break;

                    case "--ribbon":
                        a.Source = LaunchSource.Ribbon;
                        break;

                    case "--afterclose":
                        a.Source = LaunchSource.AfterClose;
                        break;

                    case "--relaunched":
                        a.Relaunched = true;
                        break;
                }
            }

            return a;
        }
    }
}