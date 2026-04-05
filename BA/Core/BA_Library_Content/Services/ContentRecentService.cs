using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BA.Core.Content.Services
{
    public sealed class ContentRecentService
    {
        private readonly string _path;

        public ContentRecentService(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public Dictionary<string, DateTime> Load()
        {
            if (!File.Exists(_path))
                return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json)
                    ?? new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void Save(Dictionary<string, DateTime> recent)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string json = JsonSerializer.Serialize(recent, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
    }
}