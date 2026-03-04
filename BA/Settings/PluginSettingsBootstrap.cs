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
        }
    }
}