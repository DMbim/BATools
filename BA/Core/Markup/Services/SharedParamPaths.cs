// BA/Core/Parameters/SharedParamPaths.cs
using System;
using System.Collections.Generic;
using System.IO;
using BA.Core.Settings;

namespace BA.Core.Parameters
{
    /// <summary>
    /// Single source of truth for BA's shared parameter file location. The path is
    /// user editable through the Plugin Settings window (SharedParamsPath field) and
    /// is persisted in PluginSettings.Strings under SettingsKeyWip2Path. DefaultWip2
    /// below only supplies the fallback for anyone who has not configured a custom
    /// path yet, or is on a fresh install with no settings.json.
    /// The missing ".txt" extension on this path once broke Apply Finishes by Rooms
    /// in production, keep TryValidateWip2 in the loop anywhere a path from here or
    /// from user input is about to be used.
    /// </summary>
    public static class SharedParamPaths
    {
        public const string SettingsKeyWip2Path = "SharedParams.Wip2Path";

        public const string DefaultWip2 =
            @"S:\CAD\Autodesk Revit\BA_Resources\BA_Shared parameters\BA_SharedParametersWIP2.txt";

        private static string _wip2;
        private static bool _loaded;
        private static readonly object Gate = new object();

        /// <summary>
        /// Current shared parameter file path used by every BA function that reads
        /// or binds shared parameters. Loaded once per Revit session from
        /// PluginSettingsStore on first access, then kept in memory. SetWip2Path
        /// updates it immediately for the rest of the session without a restart.
        /// </summary>
        public static string WIP2
        {
            get
            {
                EnsureLoaded();
                return _wip2;
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;

            lock (Gate)
            {
                if (_loaded) return;

                try
                {
                    var settings = PluginSettingsStore.Load();
                    _wip2 = settings.GetString(SettingsKeyWip2Path, DefaultWip2);
                }
                catch
                {
                    // A bad settings.json must never take down every function that
                    // reads shared parameters. Fall back to the known good default.
                    _wip2 = DefaultWip2;
                }

                if (string.IsNullOrWhiteSpace(_wip2))
                    _wip2 = DefaultWip2;

                _loaded = true;
            }
        }

        /// <summary>
        /// Called by PluginSettingsWindow on Apply and Save so the new path takes
        /// effect immediately for the rest of the Revit session. Does not write to
        /// disk itself, PluginSettingsStore.Save handles persistence separately.
        /// </summary>
        public static void SetWip2Path(string path)
        {
            lock (Gate)
            {
                _wip2 = string.IsNullOrWhiteSpace(path) ? DefaultWip2 : path.Trim();
                _loaded = true;
            }
        }

        public static bool Wip2FileExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        public static bool Wip2HasTxtExtension(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Warn, do not block, validation for the Plugin Settings UI. Returns true
        /// with no warning if the path looks correct. Returns false with a human
        /// readable warning otherwise, the caller decides whether to still allow
        /// saving.
        /// </summary>
        public static bool TryValidateWip2(string path, out string warning)
        {
            warning = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                warning = "No path set. The default will be used until you enter one.";
                return false;
            }

            var problems = new List<string>();

            if (!Wip2FileExists(path))
                problems.Add("file not found at this path");

            if (!Wip2HasTxtExtension(path))
                problems.Add("file does not end in .txt");

            if (problems.Count == 0)
                return true;

            warning = "Warning: " + string.Join(", ", problems) +
                       ". Functions using this path may fail until it is corrected.";
            return false;
        }
    }
}