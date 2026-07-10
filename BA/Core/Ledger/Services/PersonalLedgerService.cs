using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using BA.BAApplication;

namespace BA.Core.Ledger
{
    /// <summary>
    /// Each user's local record of "the value I last knew about, from the Main Ledger, for
    /// each tracked field." This is the merge-base in a three-way merge (base/local/remote),
    /// not a copy of the Main Ledger itself.
    ///
    /// CRITICAL: scoped per CENTRAL MODEL (via Document.WorksharingCentralGUID), not just per
    /// Windows user. A single user can have multiple different central models open at once
    /// (exactly the 5-building test scenario), and each one needs its own independent baseline.
    /// A single shared file keyed only by user would let syncing one document silently corrupt
    /// the merge-base for another, causing a stale local value to look like "no remote change
    /// since baseline" and get pushed over a genuinely newer remote value. This is not a
    /// hypothetical, it's exactly what was happening.
    ///
    /// Local file, single owner per central, no concurrent writers expected across different
    /// centrals since each gets its own file, so no exclusive-lock retry logic needed here,
    /// unlike LedgerFileService.
    /// </summary>
    public static class PersonalLedgerService
    {
        private static readonly string BaseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BA", "Ledger");

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static TypeDataLedger Load(Document doc)
        {
            string filePath = ResolveFilePath(doc);

            try
            {
                if (!File.Exists(filePath))
                {
                    return new TypeDataLedger();
                }

                string json = File.ReadAllText(filePath);
                TypeDataLedger ledger = JsonSerializer.Deserialize<TypeDataLedger>(json);
                ledger = ledger ?? new TypeDataLedger();
                ledger.NormalizeComparers();
                return ledger;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("PersonalLedgerService.Load: failed to read, treating as empty baseline", ex);
                return new TypeDataLedger();
            }
        }

        public static void Save(Document doc, TypeDataLedger ledger)
        {
            string filePath = ResolveFilePath(doc);

            try
            {
                if (!Directory.Exists(BaseDirectory))
                {
                    Directory.CreateDirectory(BaseDirectory);
                }

                string json = JsonSerializer.Serialize(ledger, SerializerOptions);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("PersonalLedgerService.Save: failed to write baseline", ex);
                throw;
            }
        }

        /// <summary>
        /// Priority order: (1) manual identifier via CentralIdentifierService -- always
        /// reliable, works regardless of hosting. (2) GetWorksharingCentralModelPath() -- the
        /// correct API for traditional file-based/network-share centrals. (3)
        /// WorksharingCentralGUID -- CONFIRMED to throw InapplicableDataException
        /// ("The current document is a file-based model.") for anything that isn't a cloud
        /// (BIM 360/ACC) model, per direct testing. Kept as a last-resort attempt only in case
        /// this project ever moves a model to the cloud, where it would actually apply; for a
        /// purely file-based project like this one it will always fail, which is expected and
        /// fine, that's why it's tried last with everything already covered ahead of it.
        /// </summary>
        private static string ResolveFilePath(Document doc)
        {
            string manualId = CentralIdentifierService.GetIdentifier(doc);
            if (!string.IsNullOrWhiteSpace(manualId))
            {
                string sanitized = SanitizeForFileName(manualId);
                AppLogger.LogInfo($"PersonalLedgerService.ResolveFilePath: using manual identifier '{manualId}' for document '{doc.Title}'.");
                return Path.Combine(BaseDirectory, $"PersonalLedger_{sanitized}.json");
            }

            string centralPathIdentifier = TryGetCentralModelPathIdentifier(doc);
            if (!string.IsNullOrWhiteSpace(centralPathIdentifier))
            {
                AppLogger.LogInfo($"PersonalLedgerService.ResolveFilePath: using central model path identifier for document '{doc.Title}'.");
                return Path.Combine(BaseDirectory, $"PersonalLedger_{centralPathIdentifier}.json");
            }

            string fileName;

            try
            {
                Guid centralGuid = doc.WorksharingCentralGUID;
                AppLogger.LogInfo($"PersonalLedgerService.ResolveFilePath: WorksharingCentralGUID = '{centralGuid}' for document '{doc.Title}'.");

                fileName = centralGuid != Guid.Empty
                    ? $"PersonalLedger_{centralGuid:N}.json"
                    : "PersonalLedger_unknown.json";
            }
            catch (Exception ex)
            {
                // Expected/confirmed for any file-based (non-cloud) model -- this is not an
                // unexpected failure for this project, it's the documented behavior of this
                // property. Logged at LogInfo, not LogError, since it's not actually wrong.
                AppLogger.LogInfo($"PersonalLedgerService.ResolveFilePath: WorksharingCentralGUID not applicable for '{doc.Title}' (expected for file-based models): {ex.Message}");
                fileName = "PersonalLedger_unknown.json";
            }

            return Path.Combine(BaseDirectory, fileName);
        }

        /// <summary>
        /// Correct API for identifying a traditional file-based central, unlike the cloud-only
        /// WorksharingCentralGUID. Returns a sanitized, hashed identifier derived from the
        /// central's actual UNC/network path, or null if this document isn't workshared or the
        /// central path can't be resolved (e.g. mid-detach, or not yet saved as central).
        /// </summary>
        private static string TryGetCentralModelPathIdentifier(Document doc)
        {
            try
            {
                if (!doc.IsWorkshared)
                {
                    return null;
                }

                ModelPath modelPath = doc.GetWorksharingCentralModelPath();
                if (modelPath == null)
                {
                    return null;
                }

                string userVisiblePath = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                if (string.IsNullOrWhiteSpace(userVisiblePath))
                {
                    return null;
                }

                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(userVisiblePath.ToUpperInvariant()));
                    return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogInfo($"PersonalLedgerService.TryGetCentralModelPathIdentifier: could not resolve central path for '{doc.Title}': {ex.Message}");
                return null;
            }
        }

        private static string SanitizeForFileName(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }
            return value;
        }
    }
}
