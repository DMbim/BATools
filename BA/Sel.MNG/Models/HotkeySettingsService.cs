
using BATools.SelectionManager.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BATools.SelectionManager.Services
{
    public static class HotkeySettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BATools", "hotkey_settings.json");

        public static HotkeySettings Load()
        {
            if (!File.Exists(FilePath)) return new HotkeySettings();
            try
            {
                return JsonSerializer.Deserialize<HotkeySettings>(
                    File.ReadAllText(FilePath), JsonOptions) ?? new HotkeySettings();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HotkeySettingsService] Load failed: {ex.Message}");
                return new HotkeySettings();
            }
        }

        public static void Save(HotkeySettings settings)
        {
            string path = FilePath;
            try
            {
                System.IO.Directory.CreateDirectory(
                    System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.WriteAllText(path,
                    JsonSerializer.Serialize(settings, JsonOptions));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HotkeySettingsService] Save failed: {ex.Message}");
            }
        }

        // VK code constants
        private const uint VK_SHIFT = 0x10; private const uint VK_LSHIFT = 0xA0; private const uint VK_RSHIFT = 0xA1;
        private const uint VK_CONTROL = 0x11; private const uint VK_LCTRL = 0xA2; private const uint VK_RCTRL = 0xA3;
        private const uint VK_MENU = 0x12; private const uint VK_LALT = 0xA4; private const uint VK_RALT = 0xA5;

        public static Func<uint, bool> GetTogglePredicate(ToggleShortcut shortcut) => shortcut switch
        {
            ToggleShortcut.DoubleShift => vk => vk == VK_SHIFT || vk == VK_LSHIFT || vk == VK_RSHIFT,
            ToggleShortcut.DoubleCtrl => vk => vk == VK_CONTROL || vk == VK_LCTRL || vk == VK_RCTRL,
            ToggleShortcut.DoubleAlt => vk => vk == VK_MENU || vk == VK_LALT || vk == VK_RALT,
            _ => _ => false
        };

        /// <summary>Returns null when freeze is Disabled.</summary>
        public static Func<uint, bool>? GetFreezePredicate(FreezeShortcut shortcut) => shortcut switch
        {
            FreezeShortcut.ShiftHold => vk => vk == VK_SHIFT || vk == VK_LSHIFT || vk == VK_RSHIFT,
            FreezeShortcut.CtrlHold => vk => vk == VK_CONTROL || vk == VK_LCTRL || vk == VK_RCTRL,
            FreezeShortcut.AltHold => vk => vk == VK_MENU || vk == VK_LALT || vk == VK_RALT,
            _ => null
        };
    }
}