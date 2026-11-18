// File: BA/Core/CORE_Settings/AutoSaveGate.cs
using System;
using System.Globalization;

namespace BA.Core.Settings
{
    /// <summary>
    /// Runtime switch and interval for BA Autosave. Plain static state in the
    /// same style as QuickToolbarGate, so the settings window, the toggle
    /// registry and the bootstrap can all set it without referencing the
    /// AutoSaveService itself. Read on every timer tick, so changes made in
    /// BA Settings take effect immediately without restarting Revit.
    /// </summary>
    public static class AutoSaveGate
    {
        public const string SettingsKeyEnabled = "AutoSave.Enabled";
        public const string SettingsKeyIntervalMinutes = "AutoSave.IntervalMinutes";

        public const bool DefaultEnabled = true;
        public const int DefaultIntervalMinutes = 60;
        public const int MinIntervalMinutes = 5;
        public const int MaxIntervalMinutes = 480;

        private static int _intervalMinutes = DefaultIntervalMinutes;

        public static bool Enabled { get; set; } = DefaultEnabled;

        public static int IntervalMinutes
        {
            get => _intervalMinutes;
            set => _intervalMinutes = Clamp(value);
        }

        public static int Clamp(int minutes)
        {
            return Math.Min(MaxIntervalMinutes, Math.Max(MinIntervalMinutes, minutes));
        }

        /// <summary>
        /// Converts the double stored in PluginSettings.Doubles into a valid
        /// interval. Corrupt or out of range values fall back or clamp.
        /// </summary>
        public static int FromStoredValue(double stored)
        {
            if (double.IsNaN(stored) || double.IsInfinity(stored))
                return DefaultIntervalMinutes;

            if (stored > int.MaxValue) return MaxIntervalMinutes;
            if (stored < int.MinValue) return MinIntervalMinutes;

            return Clamp((int)Math.Round(stored));
        }

        /// <summary>
        /// Parses the interval typed into BA Settings.
        /// Returns true only for a whole number inside the allowed range.
        /// minutes always receives the value that will actually be applied:
        /// the clamped number when the text was numeric but out of range,
        /// or the current runtime value when the text was not a number.
        /// </summary>
        public static bool TryParseMinutes(string? text, out int minutes, out string warning)
        {
            string s = (text ?? "").Trim();

            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                && !int.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
            {
                minutes = IntervalMinutes;
                warning = $"Enter a whole number of minutes between {MinIntervalMinutes} and {MaxIntervalMinutes}. " +
                          $"The current value of {IntervalMinutes} minutes will be kept.";
                return false;
            }

            minutes = Clamp(parsed);

            if (minutes != parsed)
            {
                warning = $"Allowed range is {MinIntervalMinutes} to {MaxIntervalMinutes} minutes. " +
                          $"This will be saved as {minutes}.";
                return false;
            }

            warning = "";
            return true;
        }
    }
}