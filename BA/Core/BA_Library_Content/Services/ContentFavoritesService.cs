using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BA.Core.Content.Services
{
    public sealed class ContentFavoritesService
    {
        private readonly string _path;

        public ContentFavoritesService(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public HashSet<string> Load()
        {
            if (!File.Exists(_path))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string json = File.ReadAllText(_path);
                var ids = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                return new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void Save(IEnumerable<string> ids)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string json = JsonSerializer.Serialize(ids.Distinct().OrderBy(x => x).ToList(), new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_path, json);
        }
    }
}