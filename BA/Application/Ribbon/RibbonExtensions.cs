using System;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI.Selection;
using Nice3point.Revit.Toolkit;
using BA.BAApplication.CommandRegistry;

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
        ///
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

            if (text == null)
            {
                text = internalName;
            }

            var data = new PushButtonData(internalName, text, assemblyPath, className);
            var button = panel.AddItem(data) as PushButton;
            if (button == null) return null;

            if (!string.IsNullOrWhiteSpace(longDescription))
                button.LongDescription = longDescription;

            if (!string.IsNullOrWhiteSpace(smallImagePath))
                button.SetImage(NormalizePackUri(smallImagePath));

            if (!string.IsNullOrWhiteSpace(largeImagePath))
                button.SetLargeImage(NormalizePackUri(largeImagePath));

            BACommandRegistry.Register(new BACommandInfo
            {
                InternalName = internalName,
                DisplayName = CleanRibbonText(text),
                Category = panel.Name ?? "Plugin",
                FullClassName = className ?? string.Empty,
                SmallIconPath = smallImagePath ?? string.Empty,
                LargeIconPath = largeImagePath ?? string.Empty,
                ShowInIssueReporter = BACommandRegistry.ShouldShowInIssueReporter(internalName)
            });

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

            if (text == null)
            {
                text = internalName;
            }

            var data = new PushButtonData(internalName, text, assemblyPath, className);
            var button = pulldown.AddPushButton(data);
            if (button == null) return null;

            if (!string.IsNullOrWhiteSpace(longDescription))
                button.LongDescription = longDescription;

            if (!string.IsNullOrWhiteSpace(smallImagePath))
                button.SetImage(NormalizePackUri(smallImagePath));

            if (!string.IsNullOrWhiteSpace(largeImagePath))
                button.SetLargeImage(NormalizePackUri(largeImagePath));

            BACommandRegistry.Register(new BACommandInfo
            {
                InternalName = internalName,
                DisplayName = CleanRibbonText(text),
                Category = "Plugin",
                FullClassName = className ?? string.Empty,
                SmallIconPath = smallImagePath ?? string.Empty,
                LargeIconPath = largeImagePath ?? string.Empty,
                ShowInIssueReporter = BACommandRegistry.ShouldShowInIssueReporter(internalName)
            });

            return button;
        }

/// <summary>
        /// Adds two stacked push buttons to the panel.
        /// Returns a tuple of (top, bottom) buttons.
        /// </summary>
        public static (PushButton top, PushButton bottom) AddStackedButtons<TCommand1, TCommand2>(
            this RibbonPanel panel,
            string internalName1, string text1,
            string internalName2, string text2,
            string smallImagePath1 = null, string smallImagePath2 = null,
            string longDescription1 = null, string longDescription2 = null)
            where TCommand1 : IExternalCommand
            where TCommand2 : IExternalCommand
        {
            var data1 = new PushButtonData(
                internalName1, text1 ?? internalName1,
                typeof(TCommand1).Assembly.Location,
                typeof(TCommand1).FullName);

            var data2 = new PushButtonData(
                internalName2, text2 ?? internalName2,
                typeof(TCommand2).Assembly.Location,
                typeof(TCommand2).FullName);

            var stacked = panel.AddStackedItems(data1, data2);

            var btn1 = stacked[0] as PushButton;
            var btn2 = stacked[1] as PushButton;

            if (btn1 != null)
            {
                if (!string.IsNullOrWhiteSpace(longDescription1))
                    btn1.LongDescription = longDescription1;
                if (!string.IsNullOrWhiteSpace(smallImagePath1))
                    btn1.SetImage(NormalizePackUri(smallImagePath1));

                BACommandRegistry.Register(new BACommandInfo
                {
                    InternalName = internalName1,
                    DisplayName = CleanRibbonText(text1),
                    Category = panel.Name ?? "Plugin",
                    FullClassName = typeof(TCommand1).FullName ?? string.Empty,
                    SmallIconPath = smallImagePath1 ?? string.Empty,
                    LargeIconPath = string.Empty,
                    ShowInIssueReporter = BACommandRegistry.ShouldShowInIssueReporter(internalName1)
                });
            }

            if (btn2 != null)
            {
                if (!string.IsNullOrWhiteSpace(longDescription2))
                    btn2.LongDescription = longDescription2;
                if (!string.IsNullOrWhiteSpace(smallImagePath2))
                    btn2.SetImage(NormalizePackUri(smallImagePath2));

                BACommandRegistry.Register(new BACommandInfo
                {
                    InternalName = internalName2,
                    DisplayName = CleanRibbonText(text2),
                    Category = panel.Name ?? "Plugin",
                    FullClassName = typeof(TCommand2).FullName ?? string.Empty,
                    SmallIconPath = smallImagePath2 ?? string.Empty,
                    LargeIconPath = string.Empty,
                    ShowInIssueReporter = BACommandRegistry.ShouldShowInIssueReporter(internalName2)
                });
            }

            return (btn1, btn2);
        }

        /// <summary>
        /// Adds three stacked push buttons to the panel.
        /// Returns a tuple of (top, middle, bottom) buttons.
        /// </summary>
        public static (PushButton top, PushButton middle, PushButton bottom) AddStackedButtons<TCommand1, TCommand2, TCommand3>(
            this RibbonPanel panel,
            string internalName1, string text1,
            string internalName2, string text2,
            string internalName3, string text3,
            string smallImagePath1 = null, string smallImagePath2 = null, string smallImagePath3 = null,
            string longDescription1 = null, string longDescription2 = null, string longDescription3 = null)
            where TCommand1 : IExternalCommand
            where TCommand2 : IExternalCommand
            where TCommand3 : IExternalCommand
        {
            var data1 = new PushButtonData(
                internalName1, text1 ?? internalName1,
                typeof(TCommand1).Assembly.Location,
                typeof(TCommand1).FullName);

            var data2 = new PushButtonData(
                internalName2, text2 ?? internalName2,
                typeof(TCommand2).Assembly.Location,
                typeof(TCommand2).FullName);

            var data3 = new PushButtonData(
                internalName3, text3 ?? internalName3,
                typeof(TCommand3).Assembly.Location,
                typeof(TCommand3).FullName);

            var stacked = panel.AddStackedItems(data1, data2, data3);

            var btn1 = stacked[0] as PushButton;
            var btn2 = stacked[1] as PushButton;
            var btn3 = stacked[2] as PushButton;

            void Register(PushButton btn, string name, string text, string icon, Type cmdType)
            {
                if (btn == null) return;
                if (!string.IsNullOrWhiteSpace(icon))
                    btn.SetImage(NormalizePackUri(icon));
                BACommandRegistry.Register(new BACommandInfo
                {
                    InternalName = name,
                    DisplayName = CleanRibbonText(text),
                    Category = panel.Name ?? "Plugin",
                    FullClassName = cmdType.FullName ?? string.Empty,
                    SmallIconPath = icon ?? string.Empty,
                    LargeIconPath = string.Empty,
                    ShowInIssueReporter = BACommandRegistry.ShouldShowInIssueReporter(name)
                });
            }

            if (btn1 != null && !string.IsNullOrWhiteSpace(longDescription1))
                btn1.LongDescription = longDescription1;
            if (btn2 != null && !string.IsNullOrWhiteSpace(longDescription2))
                btn2.LongDescription = longDescription2;
            if (btn3 != null && !string.IsNullOrWhiteSpace(longDescription3))
                btn3.LongDescription = longDescription3;

            Register(btn1, internalName1, text1, smallImagePath1, typeof(TCommand1));
            Register(btn2, internalName2, text2, smallImagePath2, typeof(TCommand2));
            Register(btn3, internalName3, text3, smallImagePath3, typeof(TCommand3));

            return (btn1, btn2, btn3);
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
        private static string CleanRibbonText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("  ", " ")
                .Trim();
        }
    }
}
