// BA/Markup/Settings/MarkupCentralPathUtility.cs
using System;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using BA.BAApplication;

namespace BA.Markup.Settings
{
    /// <summary>
    /// Single shared implementation of "hash a central model's user-visible path into a
    /// stable, filesystem-safe identifier". Used by both MarkupUserRegistryService (shared
    /// team registry, keyed per central) and MarkupBaselineService (per-user local baseline,
    /// also keyed per central). Factored out specifically so both stay identical; if this
    /// logic diverges between the two, a user's baseline file and the shared registry file
    /// silently stop referring to the same central.
    ///
    /// NOT the same thing as CentralIdentifierService, which is Ledger-specific and tracks
    /// merge-base state, not a general-purpose central identity hash.
    /// </summary>
    public static class MarkupCentralPathUtility
    {
        /// <summary>
        /// Returns a 16-character hex hash of the central's user-visible path, or null if
        /// the document is not workshared or the central path cannot be resolved. Case
        /// insensitive: two path strings differing only by case resolve to the same hash.
        /// </summary>
        public static string TryGetCentralHash(Document doc)
        {
            try
            {
                if (doc == null || !doc.IsWorkshared)
                    return null;

                ModelPath modelPath = doc.GetWorksharingCentralModelPath();
                if (modelPath == null)
                    return null;

                string userVisiblePath = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                if (string.IsNullOrWhiteSpace(userVisiblePath))
                    return null;

                var bytes = Encoding.UTF8.GetBytes(userVisiblePath.Trim().ToUpperInvariant());
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(bytes);
                return Convert.ToHexString(hash)[..16];
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MarkupCentralPathUtility.TryGetCentralHash", ex);
                return null;
            }
        }
    }
}