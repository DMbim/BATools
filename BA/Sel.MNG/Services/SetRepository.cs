using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BATools.SelectionManager.Models;

namespace BATools.SelectionManager.Services
{
    public class SetRepository
    {
        private static readonly SetRepository _instance = new();
        public static SetRepository Instance => _instance;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly Dictionary<string, List<SelectionSet>> _store = new();
        private string _activeFingerprint = string.Empty;

        private string StorageDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BATools", "SelectionSets");

        private string FilePath(string fingerprint) =>
            Path.Combine(StorageDirectory, $"{fingerprint}.json");

        private SetRepository() { }

        public void LoadForDocument(string fingerprint)
        {
            _activeFingerprint = fingerprint;

            if (_store.ContainsKey(fingerprint))
                return;

            string path = FilePath(fingerprint);

            if (!File.Exists(path))
            {
                _store[fingerprint] = new List<SelectionSet>();
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var sets = JsonSerializer.Deserialize<List<SelectionSet>>(json, _jsonOptions)
                           ?? new List<SelectionSet>();
                _store[fingerprint] = sets;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SetRepository] Load failed: {ex.Message}");
                _store[fingerprint] = new List<SelectionSet>();
            }
        }

        public void SaveToDisk(string fingerprint)
        {
            if (!_store.ContainsKey(fingerprint))
                return;

            try
            {
                Directory.CreateDirectory(StorageDirectory);
                string json = JsonSerializer.Serialize(_store[fingerprint], _jsonOptions);
                File.WriteAllText(FilePath(fingerprint), json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SetRepository] Save failed: {ex.Message}");
            }
        }

        public List<SelectionSet> GetAll(string? fingerprint = null)
        {
            string fp = fingerprint ?? _activeFingerprint;
            return _store.TryGetValue(fp, out var list) ? new List<SelectionSet>(list) : new List<SelectionSet>();
        }

        public SelectionSet? GetById(Guid id, string? fingerprint = null)
        {
            return GetAll(fingerprint).FirstOrDefault(s => s.Id == id);
        }

        public void Add(SelectionSet set)
        {
            if (!_store.ContainsKey(_activeFingerprint))
                _store[_activeFingerprint] = new List<SelectionSet>();

            set.DocumentFingerprint = _activeFingerprint;
            _store[_activeFingerprint].Add(set);
            SaveToDisk(_activeFingerprint);
        }

        public void Update(SelectionSet set)
        {
            if (!_store.TryGetValue(_activeFingerprint, out var list))
                return;

            int idx = list.FindIndex(s => s.Id == set.Id);
            if (idx < 0) return;

            set.Modified = DateTime.UtcNow;
            list[idx] = set;
            SaveToDisk(_activeFingerprint);
        }

        public void Delete(Guid id)
        {
            if (!_store.TryGetValue(_activeFingerprint, out var list))
                return;

            list.RemoveAll(s => s.Id == id);
            SaveToDisk(_activeFingerprint);
        }

        public void MarkHealth(Guid setId, SetHealthStatus status, int staleCount)
        {
            if (!_store.TryGetValue(_activeFingerprint, out var list))
                return;

            var set = list.FirstOrDefault(s => s.Id == setId);
            if (set == null) return;

            set.HealthStatus = status;
            set.StaleCount = staleCount;
            // Don't write to disk on health update — in-memory only
        }

        public void FlushAll()
        {
            foreach (var kvp in _store)
                SaveToDisk(kvp.Key);
        }
    }
}