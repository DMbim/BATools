using BA.Core.Settings;

namespace BA.App.Settings
{
    public static class PluginSettingsBootstrap
    {
        public static void ApplySavedSettingsToRuntime()
        {
            var settings = PluginSettingsStore.Load();
            var bindings = PluginToggleRegistry.Build();

            foreach (var b in bindings)
            {
                var value = settings.GetBool(b.Key, b.DefaultValue);
                b.Setter(value);
            }

            // Non bool settings that have no ToggleBinding row.
            AutoSaveGate.IntervalMinutes = AutoSaveGate.FromStoredValue(
                settings.GetDouble(AutoSaveGate.SettingsKeyIntervalMinutes, AutoSaveGate.DefaultIntervalMinutes));
        }
    }
}