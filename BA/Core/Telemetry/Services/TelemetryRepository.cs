using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BA.Telemetry.Models;

namespace BA.Telemetry.Services
{
    public class TelemetryRepository
    {
        private readonly string _networkFolder;
        private readonly string _localFolder;
        private readonly object _writeLock = new object();

        private const string NetworkBase = @"S:\CAD\Autodesk Revit\_admin\BA_tools\BA_Report";

        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        public TelemetryRepository()
        {
            string userName = Environment.UserName;

            // Network path — per-user subfolder
            _networkFolder = Path.Combine(NetworkBase, userName);

            // Local fallback path
            _localFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BA", "Telemetry", userName
            );

            EnsureDirectory(_localFolder);
            // Do not attempt to create network folder here — defer to write time
            // so a missing share does not block startup
        }

        public void Append(TelemetryEventModel telemetryEvent)
        {
            if (telemetryEvent == null)
                return;

            string fileName = $"telemetry_{DateTime.UtcNow:yyyy-MM-dd}_{Environment.UserName}.jsonl";
            string line = JsonSerializer.Serialize(telemetryEvent, _jsonOptions) + Environment.NewLine;

            bool networkWritten = false;

            // Primary — network share
            try
            {
                EnsureDirectory(_networkFolder);
                string networkPath = Path.Combine(_networkFolder, fileName);

                lock (_writeLock)
                {
                    File.AppendAllText(networkPath, line);
                }

                networkWritten = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[BA.Telemetry] Network write failed, falling back to local: {ex.Message}");
            }

            // Fallback — local AppData
            // Always write locally as well so data is never lost when share is unreachable
            try
            {
                string localPath = Path.Combine(_localFolder, fileName);

                lock (_writeLock)
                {
                    File.AppendAllText(localPath, line);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[BA.Telemetry] Local write failed: {ex.Message}");
            }
        }

        public List<TelemetryEventModel> ReadDay(DateTime date, string windowsUser = null)
        {
            var results = new List<TelemetryEventModel>();
            string user = windowsUser ?? Environment.UserName;
            string fileName = $"telemetry_{date:yyyy-MM-dd}_{user}.jsonl";

            // Prefer network data for reads — it is the authoritative source
            string networkPath = Path.Combine(NetworkBase, user, fileName);
            string localPath = Path.Combine(_localFolder, fileName);

            string pathToRead = File.Exists(networkPath) ? networkPath : localPath;

            if (!File.Exists(pathToRead))
                return results;

            try
            {
                foreach (string rawLine in File.ReadLines(pathToRead))
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    try
                    {
                        var evt = JsonSerializer.Deserialize<TelemetryEventModel>(rawLine, _jsonOptions);
                        if (evt != null)
                            results.Add(evt);
                    }
                    catch
                    {
                        // Skip malformed lines
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BA.Telemetry] Read failed: {ex.Message}");
            }

            return results;
        }

        public List<TelemetryEventModel> ReadRange(DateTime from, DateTime to, string windowsUser = null)
        {
            var results = new List<TelemetryEventModel>();

            for (DateTime date = from.Date; date <= to.Date; date = date.AddDays(1))
            {
                results.AddRange(ReadDay(date, windowsUser));
            }

            return results;
        }

        private void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        public string NetworkFolder => _networkFolder;
        public string LocalFolder => _localFolder;
    }
}