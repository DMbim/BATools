using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace BA.Core.Content.Services
{
    /// <summary>
    /// Resolves the identity used to key loaded-family favorites/tags:
    /// central model path (workshared) or local file path (non-workshared),
    /// hashed to a stable folder-safe token, plus project set detection
    /// for the network favorites path.
    ///
    /// Must be called from Revit API context (via AppExternalInvoker), not
    /// directly from the WPF thread.
    ///
    /// NOTE: hashing and project-set regex are self-contained here because
    /// MarkupCentralPathUtility's and the Type Data Ledger's project-set
    /// detector's real source were not available to reuse directly. Swap
    /// this out for the real utilities if/when provided; the regex pattern
    /// (^\d{2}-\d{3}$) matches the one already documented for the Ledger.
    /// </summary>
    public sealed class LoadedFamilyIdentity
    {
        public bool IsWorkshared { get; init; }
        public string SourcePath { get; init; } = string.Empty;
        public string IdentityHash { get; init; } = string.Empty;
        public string? ProjectSet { get; init; }
    }

    public static class LoadedFamilyIdentityResolver
    {
        private static readonly Regex ProjectSetPattern = new(@"^\d{2}-\d{3}$", RegexOptions.Compiled);

        public static LoadedFamilyIdentity Resolve(Document doc)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            bool isWorkshared = doc.IsWorkshared;
            string sourcePath;

            if (isWorkshared)
            {
                ModelPath centralModelPath = doc.GetWorksharingCentralModelPath();
                sourcePath = ModelPathUtils.ConvertModelPathToUserVisiblePath(centralModelPath);
            }
            else
            {
                sourcePath = doc.PathName ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                // Unsaved / never-saved document: no stable identity available.
                sourcePath = $"UNSAVED::{doc.Title}";
            }

            string hash = ComputeHash(sourcePath);
            string? projectSet = DetectProjectSet(sourcePath);

            return new LoadedFamilyIdentity
            {
                IsWorkshared = isWorkshared,
                SourcePath = sourcePath,
                IdentityHash = hash,
                ProjectSet = projectSet
            };
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(input.ToLowerInvariant());
            byte[] hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        private static string? DetectProjectSet(string path)
        {
            string[] segments = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string segment in segments)
            {
                if (ProjectSetPattern.IsMatch(segment))
                    return segment;
            }

            return null;
        }
    }
}