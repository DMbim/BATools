using System;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI.Selection;
using Nice3point.Revit.Toolkit;


namespace BA.Ribbon
{
    /// <summary>
    /// Helper extensions to create ribbon buttons with a unified API.
    /// </summary>
    public static class RibbonExtensions
    {
        
        /// <summary>
        /// Adds a push button to the given panel, wired to TCommand.
        /// </summary>
        public static PushButton AddPushButton<TCommand>(
            this RibbonPanel panel,
            string internalName,
            string text,
            string longDescription,
            string smallImagePath = null,
            string largeImagePath = null)
            where TCommand : IExternalCommand
        {
            string assemblyPath = typeof(TCommand).Assembly.Location;
            string className = typeof(TCommand).FullName;

            var data = new PushButtonData(internalName, text, assemblyPath, className);
            var button = panel.AddItem(data) as PushButton;
            if (button == null) return null;

            if (!string.IsNullOrWhiteSpace(longDescription))
                button.LongDescription = longDescription;

            if (!string.IsNullOrWhiteSpace(smallImagePath))
                button.SetImage(NormalizePackUri(smallImagePath));

            if (!string.IsNullOrWhiteSpace(largeImagePath))
                button.SetLargeImage(NormalizePackUri(largeImagePath));

            return button;
        }

        /// <summary>
        /// Adds a pulldown button to the given panel, wired to TCommand as default action.
        /// </summary>
        public static PulldownButton AddPulldownButton<TCommand>(
            this RibbonPanel panel,
            string internalName,
            string text,
            string longDescription,
            string smallImagePath = null,
            string largeImagePath = null)
            where TCommand : IExternalCommand
        {
            // For pulldown, Revit doesn't require the command in the data,
            // we wire TCommand as the default action via the manifest (or we just use it for naming consistency).
            var data = new PulldownButtonData(internalName, text);

            var pulldown = panel.AddItem(data) as PulldownButton;
            if (pulldown == null) return null;

            if (!string.IsNullOrWhiteSpace(longDescription))
                pulldown.LongDescription = longDescription;

            if (!string.IsNullOrWhiteSpace(smallImagePath))
                pulldown.SetImage(NormalizePackUri(smallImagePath));

            if (!string.IsNullOrWhiteSpace(largeImagePath))
                pulldown.SetLargeImage(NormalizePackUri(largeImagePath));

            return pulldown;
        }

        /// <summary>
        /// Adds a push button under a pulldown, wired to TCommand.
        /// </summary>
        public static PushButton AddPushButton<TCommand>(
            this PulldownButton pulldown,
            string internalName,
            string text,
            string longDescription,
            string smallImagePath = null,
            string largeImagePath = null)
            where TCommand : IExternalCommand
        {
            string assemblyPath = typeof(TCommand).Assembly.Location;
            string className = typeof(TCommand).FullName;

            var data = new PushButtonData(internalName, text, assemblyPath, className);
            var button = pulldown.AddPushButton(data);
            if (button == null) return null;

            if (!string.IsNullOrWhiteSpace(longDescription))
                button.LongDescription = longDescription;

            if (!string.IsNullOrWhiteSpace(smallImagePath))
                button.SetImage(NormalizePackUri(smallImagePath));

            if (!string.IsNullOrWhiteSpace(largeImagePath))
                button.SetLargeImage(NormalizePackUri(largeImagePath));

            return button;
        }

        /// <summary>
        /// Ensures a pack URI starts with a leading '/' and not with typos.
        /// </summary>
        private static string NormalizePackUri(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;

            // Fix commas and missing leading slash
            path = path.Trim();
            path = path.Replace("BA,component", "BA;component");

            if (!path.StartsWith("/"))
                path = "/" + path;

            return path;
        }
    }
}
