// File: BA.Core/Standards/ViewTemplateStandardFileIo.cs
using System;
using System.IO;
using System.Text.Json;

namespace BA.Core.Standards
{
    public static class ViewTemplateStandardFileIo
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public static string DefaultFolder()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(root, "BA", "Standards", "ViewTemplates");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void Save(string path, ViewTemplateStandardFile file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));

            var folder = Path.GetDirectoryName(path);
            Directory.CreateDirectory(string.IsNullOrWhiteSpace(folder) ? DefaultFolder() : folder);

            var json = JsonSerializer.Serialize(file, JsonOpts);
            File.WriteAllText(path, json);
        }

        public static ViewTemplateStandardFile Load(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ViewTemplateStandardFile>(json, JsonOpts)
                   ?? throw new InvalidOperationException("Invalid standard file.");
        }
    }
}
