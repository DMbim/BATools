using System;
using System.IO;
using System.Text.Json;

namespace BA.Core.Rooms
{
    public static class RoomHostFinishTransferSettingsStore
    {
        private static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BA");

        private static string FilePath =>
            Path.Combine(Folder, "CeilingFloorToRoomSettings.json");

        public static RoomHostFinishTransferSettings Load()
        {
            if (!System.IO.File.Exists(FilePath))
                return new RoomHostFinishTransferSettings();

            var json = System.IO.File.ReadAllText(FilePath);
            var s = JsonSerializer.Deserialize<RoomHostFinishTransferSettings>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return s ?? new RoomHostFinishTransferSettings();
        }

        public static void Save(RoomHostFinishTransferSettings settings)
        {
            Directory.CreateDirectory(Folder);

            var json = JsonSerializer.Serialize(settings,
                new JsonSerializerOptions { WriteIndented = true });

            System.IO.File.WriteAllText(FilePath, json);
        }
    }
}