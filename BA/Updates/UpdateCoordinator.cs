// File: BA/Updates/UpdateCoordinator.cs
using Autodesk.Revit.UI;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BA.Updates
{
    internal sealed class UpdateCheckResult
    {
        public bool HasUpdate { get; set; }
        public Version Installed { get; set; } = new Version(0, 0, 0);
        public Version Latest { get; set; } = new Version(0, 0, 0);

        public string? Tag { get; set; }
        public string? ReleaseUrl { get; set; }

        public string? AssetName { get; set; }
        public string? AssetUrl { get; set; }
    }

    internal static class UpdateCoordinator
    {
        public static async Task<UpdateCheckResult?> CheckAsync(UIApplication uiapp, CancellationToken ct)
        {
            // Throttle checks (office-friendly)
            var state = UpdateStateStore.Load();
            if (state.LastCheckedUtc != default &&
                (DateTime.UtcNow - state.LastCheckedUtc) < UpdateConfig.CheckInterval)
            {
                return null;
            }

            state.LastCheckedUtc = DateTime.UtcNow;
            UpdateStateStore.Save(state);

            // Installed version comes from BATools.version (preferred) or assembly version fallback
            var installed = VersionUtil.GetInstalledVersion(typeof(UpdateCoordinator).Assembly);

            GitHubReleaseInfo? rel;
            try
            {
                rel = await GitHubReleaseClientLite.GetLatestReleaseAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // Offline / blocked: never break Revit startup
                return null;
            }

            if (rel == null || string.IsNullOrWhiteSpace(rel.tag_name))
                return null;

            if (!VersionUtil.TryParseLoose(rel.tag_name, out var latest))
                return null;

            // No update
            if (VersionUtil.Compare(latest, installed) <= 0)
            {
                return new UpdateCheckResult
                {
                    HasUpdate = false,
                    Installed = installed,
                    Latest = latest,
                    Tag = rel.tag_name,
                    ReleaseUrl = rel.html_url
                };
            }

            // Find correct asset for this Revit year (e.g. BA_R26.zip)
            var revitYear = uiapp.Application.VersionNumber; // "2026"
            var assetName = UpdateConfig.GetAssetNameForRevit(revitYear);
            var asset = GitHubReleaseClientLite.FindAsset(rel, assetName);

            return new UpdateCheckResult
            {
                HasUpdate = true,
                Installed = installed,
                Latest = latest,
                Tag = rel.tag_name,
                ReleaseUrl = rel.html_url,
                AssetName = assetName,
                AssetUrl = asset?.browser_download_url
            };
        }

        public static void PromptAndHandle(UIApplication uiapp, UpdateCheckResult r)
        {
            if (r == null || !r.HasUpdate)
                return;

            if (string.IsNullOrWhiteSpace(r.AssetUrl) || string.IsNullOrWhiteSpace(r.AssetName))
            {
                TaskDialog.Show("BA Tools Update",
                    $"New version found ({r.Latest}) but asset was not found.\n\n" +
                    $"Expected asset name:\n{r.AssetName}\n\n" +
                    $"Fix: upload that ZIP to the GitHub Release.");
                return;
            }

            var choice = UpdatePrompt.Show(uiapp, r);

            switch (choice)
            {
                case UpdateChoice.UpdateNow:
                    // CRITICAL FIX: always pass current Revit PID so installer waits for Revit to close
                    InstallerLauncher.LaunchUpdate(uiapp, r, silent: false);
                    break;

                case UpdateChoice.UpdateAfterClose:
                    InstallerLauncher.LaunchUpdate(uiapp, r, silent: true);
                    break;

                case UpdateChoice.UpdateLater:
                default:
                    // do nothing -> user will see prompt next Revit start
                    break;
            }
        }
    }
}
