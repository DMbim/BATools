// File: BA/Updates/UpdatePrompt.cs
using Autodesk.Revit.UI;

namespace BA.Updates
{
    internal static class UpdatePrompt
    {
        public static UpdateChoice Show(UIApplication uiapp, UpdateCheckResult r)
        {
            var td = new TaskDialog("BA Tools Update")
            {
                MainInstruction = $"New BA Tools version available: {r.Latest} (installed: {r.Installed})",
                MainContent = "Choose what to do:",
                CommonButtons = TaskDialogCommonButtons.Close
            };

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Update now",
                "Starts the updater now. It will wait, then update after you close Revit.");

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Update later",
                "No changes now. You’ll be prompted again next time Revit is opened.");

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Update after Revit is closed",
                "Starts the updater in waiting + silent mode. It will update automatically after Revit exits.");

            var res = td.Show();

            if (res == TaskDialogResult.CommandLink1) return UpdateChoice.UpdateNow;
            if (res == TaskDialogResult.CommandLink3) return UpdateChoice.UpdateAfterClose;
            return UpdateChoice.UpdateLater;
        }
    }
}
