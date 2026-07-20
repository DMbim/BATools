using Autodesk.Revit.UI;

namespace BA.Updates
{
    internal static class UpdatePrompt
    {
        public static UpdateChoice Show(UpdateCheckResult r)
        {
            var td = new TaskDialog("BA Tools Update")
            {
                MainInstruction = $"New BA Tools version available: {r.Latest} (installed: {r.Installed})",
                MainContent = "Choose what to do:",
                CommonButtons = TaskDialogCommonButtons.Close
            };

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Update",
                "Starts the updater now. It waits for Revit to close, then installs the new version automatically.");

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Not now",
                "Skip this version. You won't be asked again until a newer version is released.");

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

            if (res == TaskDialogResult.CommandLink1) return UpdateChoice.Update;
            return UpdateChoice.Later;
        }
    }
}