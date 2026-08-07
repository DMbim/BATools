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
        /// Manual "Check for Updates" ribbon click, when a newer version exists. Notify only:
        /// tells the person an update is available and that it installs automatically the next
        /// time Revit closes. Never launches the installer itself. Offers a Skip this version
        /// command link, which is the only way to suppress the automatic close time launch for
        /// that specific version.
        /// </summary>
        public static void NotifyOnly(UpdateCheckResult r)
        {
            if (r == null || !r.HasUpdate)
                return;

            if (!HasValidAsset(r))
            {
                ShowMissingAssetDialog(r);
                return;
            }

            var td = new TaskDialog("BA Tools Update")
            {
                MainInstruction = $"New BA Tools version available: {r.Latest} (installed: {r.Installed})",
                MainContent = "It will install automatically the next time Revit closes.",
                CommonButtons = TaskDialogCommonButtons.Close
            };

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "OK",
                "Got it. The update installs automatically next time Revit closes.");

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Skip this version",
                "Don't install this version automatically. You won't be asked again until a newer version is released.");

            if (!string.IsNullOrWhiteSpace(r.Body))
            {
                var body = r.Body.Length > 1000 ? r.Body.Substring(0, 1000) + "..." : r.Body;
                td.ExpandedContent = body;
            }

            if (!string.IsNullOrWhiteSpace(r.ReleaseUrl))
            {
                td.FooterText = $"Release notes: {r.ReleaseUrl}";
            }

            var res = td.Show();

            if (res == TaskDialogResult.CommandLink2)
            {
                var state = UpdateStateStore.Load();
                state.DismissedVersion = r.Tag;
                UpdateStateStore.Save(state);
            }
        }

        /// <summary>
        /// Fires from DocumentClosing (last open document only), when a newer version exists
        /// and was not previously skipped. No dialog: launches the installer window directly,
        /// non silent, so the person sees the installer's own UI doing the work after Revit
        /// finishes closing. Called from UpdateService.TryAutoLaunchFromCache, which already
        /// checked DismissedVersion before reaching here.
        /// </summary>
        public static void AutoLaunchOnClose(UpdateCheckResult r)
        {
            if (r == null || !r.HasUpdate)
                return;

            if (!HasValidAsset(r))
            {
                // This is the one path where the update genuinely should have happened,
                // so still tell the person rather than failing silently.
                ShowMissingAssetDialog(r);
                return;
            }

            InstallerLauncher.LaunchUpdate(r, silent: false);
        }

        private static bool HasValidAsset(UpdateCheckResult r)
            => !string.IsNullOrWhiteSpace(r.AssetUrl) && !string.IsNullOrWhiteSpace(r.AssetName);

        private static void ShowMissingAssetDialog(UpdateCheckResult r)
        {
            TaskDialog.Show("BA Tools Update",
                $"New version found ({r.Latest}) but asset was not found.\n\n" +
                $"Expected asset name:\n{r.AssetName}\n\n" +
                $"Fix: upload that ZIP to the GitHub Release.");
        }
    }
}