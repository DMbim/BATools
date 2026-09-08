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
        /// Checks for a newer release.
        ///
        /// force=false: throttled by UpdateConfig.CheckInterval. When throttled (or when the
        ///              live GitHub call fails), this does NOT return null anymore — it
        ///              reconstructs a result from the last persisted successful check
        ///              (UpdateState.LastKnownTag etc.), recomputed against the CURRENT
        ///              installed version. Only returns null if we have never successfully
        ///              checked at all (fresh install, first-ever run). This is what makes
        ///              AutoLaunchOnClose fire even when the startup Idling check was throttled.
        ///
        /// force=true:  bypasses the throttle, always hits GitHub live, propagates network
        ///              errors to the caller (manual "Check for Updates" click should report
        ///              a real failure, not silently fall back).
        /// </summary>
        public static async Task<UpdateCheckResult?> CheckAsync(UIApplication uiapp, bool force, CancellationToken ct)
        {
            var state = UpdateStateStore.Load();
            var installed = VersionUtil.GetInstalledVersion(typeof(UpdateCoordinator).Assembly);
            var revitVersion = uiapp.Application.VersionNumber; // "2026"

            bool haveAnyPersistedResult = !string.IsNullOrWhiteSpace(state.LastKnownTag);

            bool throttled = !force
                && haveAnyPersistedResult
                && state.LastCheckedUtc != default
                && (DateTime.UtcNow - state.LastCheckedUtc) < UpdateConfig.CheckInterval;

            if (throttled)
            {
                return BuildResultFromPersistedState(state, installed, revitVersion);
            }

            state.LastCheckedUtc = DateTime.UtcNow;
            UpdateStateStore.Save(state);

            GitHubReleaseInfo? rel;
            try
            {
                rel = await GitHubReleaseClientLite.GetLatestReleaseAsync(ct).ConfigureAwait(false);
            }
            catch when (!force)
            {
                // Offline / blocked during the automatic check: fall back to whatever we
                // last knew rather than going dark for the rest of this session.
                return BuildResultFromPersistedState(state, installed, revitVersion);
            }
            // NOTE: if force == true, the exception above is NOT caught by the filter and
            // propagates to the caller (UpdateService.ForceCheckAsync / Cmd_CheckForUpdates).

            if (rel == null || string.IsNullOrWhiteSpace(rel.tag_name))
                return BuildResultFromPersistedState(state, installed, revitVersion);

            if (!VersionUtil.TryParseLoose(rel.tag_name, out var latest))
                return BuildResultFromPersistedState(state, installed, revitVersion);

            // Persist what we just learned, regardless of whether it turns out to be an
            // update or not, so future throttled/offline checks have accurate data.
            state.LastKnownTag = rel.tag_name;
            state.LastKnownReleaseUrl = rel.html_url;
            state.LastKnownBody = rel.body;

            if (VersionUtil.Compare(latest, installed) <= 0)
            {
                state.LastKnownAssetName = null;
                state.LastKnownAssetUrl = null;
                state.LastKnownRevitVersion = null;
                UpdateStateStore.Save(state);

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

            var assetName = UpdateConfig.GetAssetNameForRevit(revitVersion);
            var asset = GitHubReleaseClientLite.FindAsset(rel, assetName);

            state.LastKnownAssetName = assetName;
            state.LastKnownAssetUrl = asset?.browser_download_url;
            state.LastKnownRevitVersion = revitVersion;
            UpdateStateStore.Save(state);

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
        /// Reconstructs an UpdateCheckResult from the last persisted successful check,
        /// re-evaluated against the CURRENT installed version (which can only differ from
        /// last time if the user actually updated since then). Returns null only if we
        /// have never successfully checked before at all.
        /// </summary>
        private static UpdateCheckResult? BuildResultFromPersistedState(UpdateState state, Version installed, string revitVersion)
        {
            if (string.IsNullOrWhiteSpace(state.LastKnownTag))
                return null;

            if (!VersionUtil.TryParseLoose(state.LastKnownTag, out var latest))
                return null;

            bool hasUpdate = VersionUtil.Compare(latest, installed) > 0;

            string? assetName = null;
            string? assetUrl = null;

            if (hasUpdate)
            {
                assetName = UpdateConfig.GetAssetNameForRevit(revitVersion);

                // Only trust the persisted asset URL if it was captured for THIS Revit year.
                if (string.Equals(state.LastKnownRevitVersion, revitVersion, StringComparison.OrdinalIgnoreCase))
                {
                    assetUrl = state.LastKnownAssetUrl;
                }
            }

            return new UpdateCheckResult
            {
                HasUpdate = hasUpdate,
                Installed = installed,
                Latest = latest,
                Tag = state.LastKnownTag,
                ReleaseUrl = state.LastKnownReleaseUrl,
                Body = state.LastKnownBody,
                AssetName = assetName,
                AssetUrl = assetUrl,
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