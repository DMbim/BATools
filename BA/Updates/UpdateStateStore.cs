using System;
using System.IO;
using Newtonsoft.Json;

namespace BA.Updates
{
    internal sealed class UpdateState
    {
        public DateTime LastCheckedUtc { get; set; }
        public string? LastPromptedVersion { get; set; }
        public string? DismissedVersion { get; set; } // optional if you later add "Skip this version"
    }

    internal static class UpdateStateStore
    {
        private static string GetStatePath()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(root, "BA", "BATools");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "update.state.json");
        }

        public static UpdateState Load()
        {
            try
            {
                var path = GetStatePath();
                if (!File.Exists(path)) return new UpdateState();
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<UpdateState>(json) ?? new UpdateState();
            }
            catch
            {
                return new UpdateState();
            }
        }

        public static void Save(UpdateState state)
        {
            try
            {
                var path = GetStatePath();
                var json = JsonConvert.SerializeObject(state, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch
            {
                // never block Revit startup
            }
        }
    }
}
