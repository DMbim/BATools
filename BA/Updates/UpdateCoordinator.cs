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
        public string? Body { get; set; }

        public string? AssetName { get; set; }
        public string? AssetUrl { get; set; }

        public string? RevitVersion { get; set; }
    }

    internal static class UpdateCoordinator
    {
        /// <summary>
        /// Checks GitHub for a newer release.
        /// force=false: respects UpdateConfig.CheckInterval throttle, swallows network errors
        ///              (never breaks Revit startup), returns null if throttled or offline.
        /// force=true:  bypasses throttle, propagates network errors to the caller so a manual
        ///              "Check for Updates" click can report a real failure instead of silently
        ///              falling back to a stale cached result.
        /// </summary>
        public static async Task<UpdateCheckResult?> CheckAsync(UIApplication uiapp, bool force, CancellationToken ct)
        {
            var state = UpdateStateStore.Load();
            if (!force && state.LastCheckedUtc != default &&
                (DateTime.UtcNow - state.LastCheckedUtc) < UpdateConfig.CheckInterval)
            {
                return null;
            }

            state.LastCheckedUtc = DateTime.UtcNow;
            UpdateStateStore.Save(state);

            var installed = VersionUtil.GetInstalledVersion(typeof(UpdateCoordinator).Assembly);
            var revitVersion = uiapp.Application.VersionNumber; // "2026"

            GitHubReleaseInfo? rel;
            try
            {
                rel = await GitHubReleaseClientLite.GetLatestReleaseAsync(ct).ConfigureAwait(false);
            }
            catch when (!force)
            {
                // Offline / blocked during the automatic startup check: never break Revit startup.
                return null;
            }
            // NOTE: if force == true, the exception above is NOT caught by the filter and
            // propagates to the caller (UpdateService.ForceCheckAsync), which reports it.

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
                    ReleaseUrl = rel.html_url,
                    Body = rel.body,
                    RevitVersion = revitVersion
                };
            }

            // Find correct asset for this Revit year (e.g. BA_R26.zip)
            var assetName = UpdateConfig.GetAssetNameForRevit(revitVersion);
            var asset = GitHubReleaseClientLite.FindAsset(rel, assetName);

            return new UpdateCheckResult
            {
                HasUpdate = true,
                Installed = installed,
                Latest = latest,
                Tag = rel.tag_name,
                ReleaseUrl = rel.html_url,
                Body = rel.body,
                AssetName = assetName,
                AssetUrl = asset?.browser_download_url,
                RevitVersion = revitVersion
            };
        }

        /// <summary>
        /// Shows the update dialog for a known-available update and acts on the choice.
        /// Does not take a UIApplication: everything needed (RevitVersion, AssetName, etc.)
        /// is already resolved into UpdateCheckResult by CheckAsync, since this is called
        /// from ApplicationClosing where a UIApplication is not available.
        /// </summary>
        public static void PromptAndHandle(UpdateCheckResult r)
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

            var choice = UpdatePrompt.Show(r);

            switch (choice)
            {
                case UpdateChoice.Update:
                    InstallerLauncher.LaunchUpdate(r, silent: false);
                    break;

                case UpdateChoice.Later:
                default:
                    // Don't nag again for this exact version until a newer one is released.
                    var state = UpdateStateStore.Load();
                    state.DismissedVersion = r.Tag;
                    UpdateStateStore.Save(state);
                    break;
            }
        }
    }
}