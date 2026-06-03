using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BATools.SelectionManager.Services
{
    public static class RecentActionsService
    {
        private const int MaxRecent = 10;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private static string StoragePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BATools",
            "recent_actions.json");

        private static List<string> _recents = new();
        private static bool _loaded;

        public static IReadOnlyList<string> GetRecents()
        {
            EnsureLoaded();
            return _recents.AsReadOnly();
        }

        /// <summary>
        /// Records an action execution. Moves actionId to front,
        /// evicts oldest entry if over MaxRecent. Saves immediately.
        /// </summary>
        public static void Record(string actionId)
        {
            EnsureLoaded();
            _recents.Remove(actionId);
            _recents.Insert(0, actionId);

            if (_recents.Count > MaxRecent)
                _recents = _recents.Take(MaxRecent).ToList();

            Save();
        }

        /// <summary>
        /// Invalidates the in-memory cache. Next call to GetRecents() or Record()
        /// will re-read from disk. Call this on DocumentOpened to pick up changes
        /// written by other Revit sessions sharing the same AppData file.
        /// </summary>
        public static void Reset()  // <- NEW
        {
            _recents = new List<string>();
            _loaded = false;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            string path = StoragePath;
            if (!File.Exists(path)) return;

            try
            {
                _recents = JsonSerializer.Deserialize<List<string>>(
                    File.ReadAllText(path), JsonOptions)
                    ?? new List<string>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RecentActionsService] Load failed: {ex.Message}");
                _recents = new List<string>();
            }
        }

        private static void Save()
        {
            string path = StoragePath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(_recents, JsonOptions));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RecentActionsService] Save failed: {ex.Message}");
            }
        }
    }
}