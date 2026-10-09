using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace BA.Core.Settings
{
    public enum LeaderRegistryStatus
    {
        Ok = 0,
        Empty = 1,
        Unavailable = 2
    }

    public sealed class LeaderRegistrySnapshot
    {
        public LeaderRegistryStatus Status { get; }
        public IReadOnlyList<string> Leaders { get; }
        public string Error { get; }

        public LeaderRegistrySnapshot(LeaderRegistryStatus status, IReadOnlyList<string>? leaders, string? error)
        {
            Status = status;
            Leaders = leaders ?? Array.Empty<string>();
            Error = error ?? "";
        }
    }

    /// <summary>
    /// Central list of Revit Leaders (Autodesk account names) stored as JSON on the
    /// S: admin share. No Revit dependency. Unreadable or damaged data is reported as
    /// Unavailable so callers can fail closed. Missing file in a reachable folder is
    /// reported as Empty, which allows registering the first leader.
    /// </summary>
    public static class RevitLeaderRegistry
    {
        public const string DefaultPath = @"S:\CAD\Autodesk Revit\_admin\BA_tools\BA_RevitLeaders.json";
        public const string AlternativePath = @"P:\_BIM\_Admin\BA_RevitLeaders.json";


        private const int ReadAttempts = 3;
        private const int WriteAttempts = 3;
        private const int RetryDelayMs = 150;

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private sealed class LeaderFileModel
        {
            public List<string> Leaders { get; set; } = new List<string>();
        }

        public static string Normalize(string? username)
        {
            return (username ?? "").Trim();
        }

        public static LeaderRegistrySnapshot Load(string? path = null)
        {
            string filePath = ResolvePath(path);

            try
            {
                string? dir = Path.GetDirectoryName(filePath);
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                    return new LeaderRegistrySnapshot(LeaderRegistryStatus.Unavailable, null, "Folder not reachable: " + dir);

                if (!File.Exists(filePath))
                    return new LeaderRegistrySnapshot(LeaderRegistryStatus.Empty, null, null);

                string json = ReadAllTextWithRetry(filePath);
                LeaderFileModel? model = JsonSerializer.Deserialize<LeaderFileModel>(json, JsonOpts);
                List<string> leaders = Clean(model?.Leaders);

                if (leaders.Count == 0)
                    return new LeaderRegistrySnapshot(LeaderRegistryStatus.Empty, null, null);

                return new LeaderRegistrySnapshot(LeaderRegistryStatus.Ok, leaders, null);
            }
            catch (Exception ex)
            {
                return new LeaderRegistrySnapshot(LeaderRegistryStatus.Unavailable, null, ex.Message);
            }
        }

        public static bool IsLeader(LeaderRegistrySnapshot snapshot, string? username)
        {
            if (snapshot == null) return false;
            if (snapshot.Status != LeaderRegistryStatus.Ok) return false;

            string name = Normalize(username);
            if (name.Length == 0) return false;

            return snapshot.Leaders.Any(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// A listed leader can edit the list. When the list is empty, anyone can edit it
        /// so the first leader can be registered.
        /// </summary>
        public static bool CanEdit(LeaderRegistrySnapshot snapshot, string? actingUsername)
        {
            if (snapshot == null) return false;
            if (snapshot.Status == LeaderRegistryStatus.Empty) return true;
            return IsLeader(snapshot, actingUsername);
        }

        public static bool TryAdd(string? username, string? actingUsername, out string error, string? path = null)
        {
            error = "";

            string name = Normalize(username);
            if (name.Length == 0)
            {
                error = "Enter an Autodesk username.";
                return false;
            }

            LeaderRegistrySnapshot snapshot = Load(path);

            if (snapshot.Status == LeaderRegistryStatus.Unavailable)
            {
                error = "The leader list is not available: " + snapshot.Error;
                return false;
            }

            if (!CanEdit(snapshot, actingUsername))
            {
                error = "Only listed Revit Leaders can change this list.";
                return false;
            }

            var leaders = new List<string>(snapshot.Leaders);

            if (leaders.Any(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase)))
            {
                error = name + " is already listed.";
                return false;
            }

            leaders.Add(name);
            return TrySave(leaders, out error, path);
        }

        public static bool TryRemove(string? username, string? actingUsername, out string error, string? path = null)
        {
            error = "";

            string name = Normalize(username);
            if (name.Length == 0)
            {
                error = "Select a leader to remove.";
                return false;
            }

            LeaderRegistrySnapshot snapshot = Load(path);

            if (snapshot.Status == LeaderRegistryStatus.Unavailable)
            {
                error = "The leader list is not available: " + snapshot.Error;
                return false;
            }

            if (!CanEdit(snapshot, actingUsername))
            {
                error = "Only listed Revit Leaders can change this list.";
                return false;
            }

            var leaders = new List<string>(snapshot.Leaders);
            int index = leaders.FindIndex(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                error = name + " is not in the list.";
                return false;
            }

            if (leaders.Count <= 1)
            {
                error = "The last Revit Leader cannot be removed.";
                return false;
            }

            leaders.RemoveAt(index);
            return TrySave(leaders, out error, path);
        }

        private static string ResolvePath(string? path)
        {
            return string.IsNullOrWhiteSpace(path) ? DefaultPath : path!;

        }

        private static List<string> Clean(IEnumerable<string>? names)
        {
            var result = new List<string>();
            if (names == null) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string raw in names)
            {
                string n = Normalize(raw);
                if (n.Length == 0) continue;
                if (seen.Add(n)) result.Add(n);
            }

            return result;
        }

        private static string ReadAllTextWithRetry(string filePath)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return File.ReadAllText(filePath);
                }
                catch (IOException) when (attempt < ReadAttempts)
                {
                    Thread.Sleep(RetryDelayMs);
                }
            }
        }

        private static bool TrySave(List<string> leaders, out string error, string? path)
        {
            error = "";

            string filePath = ResolvePath(path);
            string json = JsonSerializer.Serialize(new LeaderFileModel { Leaders = Clean(leaders) }, JsonOpts);

            for (int attempt = 1; attempt <= WriteAttempts; attempt++)
            {
                string tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

                try
                {
                    File.WriteAllText(tempPath, json, new UTF8Encoding(false));
                    File.Move(tempPath, filePath, true);
                    return true;
                }
                catch (UnauthorizedAccessException ex)
                {
                    error = "No permission to write the leader list: " + ex.Message;
                    return false;
                }
                catch (IOException ex)
                {
                    error = "Could not write the leader list: " + ex.Message;
                    if (attempt < WriteAttempts)
                        Thread.Sleep(RetryDelayMs);
                }
                catch (Exception ex)
                {
                    error = "Could not write the leader list: " + ex.Message;
                    return false;
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }
    }
}