using System.Collections.Generic;
using BA.Core.Settings;
using BA.App.Guards;
using BA.Core.Overhead;
using BA.App.Overhead;

namespace BA.App.Settings
{
    public static class PluginToggleRegistry
    {
        private static readonly object _lock = new();
        private static IReadOnlyList<ToggleBinding>? _cache;

        public static IReadOnlyList<ToggleBinding> Build()
        {
            if (_cache != null) return _cache;
            lock (_lock)
            {
                if (_cache != null) return _cache;
                _cache = new List<ToggleBinding>
                {
                    new ToggleBinding(
                        key: "Guards.ImportCad.Enabled",
                        group: "Guards",
                        name: "Warn on CAD import",
                        description: "Blocks/intercepts CAD import and shows a warning dialog.",
                        defaultValue: true,
                        getter: () => ImportCadWarningGuard.Enabled,
                        setter: v => ImportCadWarningGuard.Enabled = v
                    ),
                    new ToggleBinding(
                        key: "Guards.ImportCad.BindGenericImport",
                        group: "Guards",
                        name: "Bind generic import command",
                        description: "Also intercepts generic Import command (some Revit builds route CAD import through it).",
                        defaultValue: true,
                        getter: () => ImportCadWarningGuard.BindGenericImport,
                        setter: v => ImportCadWarningGuard.BindGenericImport = v
                    ),
                    // ✅ YOUR OVERHEAD AUTO PROXY TOGGLE
                    new ToggleBinding(
                        key: "Overhead.AutoProxy.Enabled",
                        group: "Annotations",
                        name: "Overhead Auto Proxy",
                        description: "Automatically creates overhead dashed proxies in floor plans.",
                        defaultValue: true,
                        getter: () => OverheadProxyUpdater.Enabled,
                        setter: v =>
                        {
                            OverheadProxyUpdater.Enabled = v;
                            // When turning OFF: do cleanup via Idling (transaction-safe)
                            if (!v)
                                OverheadToggleController.RequestDisableCleanup();
                        }
                    ),
                    new ToggleBinding(
                        key: "QuickToolbar.Enabled",
                        group: "Selection Manager",
                        name: "Quick Toolbar",
                        description: "Show the floating toolbar near the cursor after a selection.",
                        defaultValue: true,
                        getter: () => QuickToolbarGate.Enabled,
                        setter: v => QuickToolbarGate.Enabled = v
                    ),
                    // NEW: shared parameter editing guard (Revit Leaders only)
                    new ToggleBinding(
                        key: SharedParameterCreationGuard.SettingsKeyEnabled,
                        group: "Guards",
                        name: "Restrict shared parameter editing",
                        description: "Blocks the Edit Shared Parameters dialog for anyone who is not a registered Revit Leader. When off, nobody is blocked and each skipped attempt is logged.",
                        defaultValue: SharedParameterCreationGuard.DefaultEnabled,
                        getter: () => SharedParameterCreationGuard.Enabled,
                        setter: v => SharedParameterCreationGuard.Enabled = v
                    ),
                    new ToggleBinding(
                        key: AutoSaveGate.SettingsKeyEnabled,
                        group: "Documents",
                        name: "Autosave",
                        description: "Silently saves every open project once the autosave interval has passed since its last save. Workshared models save the local file only, never sync. Families, links, read only files and central files opened directly are skipped.",
                        defaultValue: AutoSaveGate.DefaultEnabled,
                        getter: () => AutoSaveGate.Enabled,
                        setter: v => AutoSaveGate.Enabled = v
                    ),
                };
                return _cache;
            }
        }

        public static void Reset() => _cache = null;
    }
}