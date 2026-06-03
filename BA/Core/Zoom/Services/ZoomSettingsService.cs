using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BATools.Zoom.Settings;
using BATools.Zoom.Views;

namespace BATools.Zoom.Services
{
    /// <summary>
    /// Owns validation and interactive configuration of ZoomToRoomSettings.
    /// Separated from commands so prompt logic is reusable across all three zoom commands.
    /// </summary>
    internal static class ZoomSettingsService
    {
        /// <summary>
        /// Validates current room ID parameter settings and optionally forces the selection prompt.
        /// When forcePrompt is false, the prompt is shown only if settings are in an invalid state:
        ///   - Shared mode with no parseable GUID
        ///   - ByName mode with no name set
        /// Settings are mutated in place. Caller is responsible for calling Save() after this returns.
        /// </summary>
        public static void EnsureRoomIdParameterSelected(
            Document sourceDoc,
            ZoomToRoomSettings settings,
            bool forcePrompt)
        {
            bool invalid =
                (string.Equals(settings.RoomIdParamMode, "Shared", StringComparison.OrdinalIgnoreCase) &&
                 !Guid.TryParse(settings.RoomIdSharedGuid, out _)) ||
                (string.Equals(settings.RoomIdParamMode, "ByName", StringComparison.OrdinalIgnoreCase) &&
                 string.IsNullOrWhiteSpace(settings.RoomIdName));

            if (!forcePrompt && !invalid) return;

            var td = new TaskDialog("Room ID Parameter");
            td.MainInstruction = "Choose the parameter used as Room ID";
            td.MainContent = "Select the built-in Number, BA_ID, a custom parameter name, or a shared parameter GUID.";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Built-in: Number");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Project parameter: BA_ID");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Project parameter: enter name...");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Shared parameter: enter GUID...");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var res = td.Show();

            if (res == TaskDialogResult.CommandLink1)
            {
                settings.RoomIdParamMode = "BuiltIn";
                settings.RoomIdName = null;
                settings.RoomIdSharedGuid = null;
                return;
            }

            if (res == TaskDialogResult.CommandLink2)
            {
                settings.RoomIdParamMode = "ByName";
                settings.RoomIdName = "BA_ID";
                settings.RoomIdSharedGuid = null;
                return;
            }

            if (res == TaskDialogResult.CommandLink3)
            {
                var name = SimpleInputWindow.Show(
                    "Room ID Parameter",
                    "Enter the parameter name (string):",
                    settings.RoomIdName ?? string.Empty);

                if (!string.IsNullOrWhiteSpace(name))
                {
                    settings.RoomIdParamMode = "ByName";
                    settings.RoomIdName = name.Trim();
                    settings.RoomIdSharedGuid = null;
                    return;
                }
            }

            if (res == TaskDialogResult.CommandLink4)
            {
                var guidText = SimpleInputWindow.Show(
                    "Shared Parameter GUID",
                    "Enter GUID (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx):",
                    settings.RoomIdSharedGuid ?? string.Empty);

                if (Guid.TryParse(guidText, out Guid g))
                {
                    settings.RoomIdParamMode = "Shared";
                    settings.RoomIdSharedGuid = g.ToString("D");
                    settings.RoomIdName = null;
                    return;
                }

                TaskDialog.Show("Room ID Parameter", "Invalid GUID. Falling back to built-in Number.");
            }

            // Covers Cancel and failed GUID entry
            settings.RoomIdParamMode = "BuiltIn";
            settings.RoomIdName = null;
            settings.RoomIdSharedGuid = null;
        }
    }
}