using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BA
{
    public class ScheduleMappingRow
    {
        public string SourceColumn { get; set; }
        public string DestinationParameter { get; set; }
    }

    public static class ScheduleSyncStore
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string GetPath()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BA");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "schedule_sync.json");
        }

        public static Dictionary<string, List<ScheduleMappingRow>> Load()
        {
            try
            {
                var path = GetPath();
                if (!File.Exists(path))
                    return new Dictionary<string, List<ScheduleMappingRow>>(StringComparer.OrdinalIgnoreCase);

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Dictionary<string, List<ScheduleMappingRow>>>(json, JsonOpts)
                    ?? new Dictionary<string, List<ScheduleMappingRow>>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, List<ScheduleMappingRow>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public static void Save(Dictionary<string, List<ScheduleMappingRow>> data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, JsonOpts);
                File.WriteAllText(GetPath(), json);
            }
            catch { }
        }
    }
}
