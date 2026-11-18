using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BA.Updates
{
    internal sealed class GitHubReleaseInfo
    {
        public string? tag_name { get; set; }
        public string? html_url { get; set; }
        public string? body { get; set; }
        public GitHubAsset[]? assets { get; set; }
    }

    internal sealed class GitHubAsset
    {
        public string? name { get; set; }
        public string? browser_download_url { get; set; }
    }

    internal static class GitHubReleaseClientLite
    {
        public static async Task<GitHubReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }

            var url = $"https://api.github.com/repos/{UpdateConfig.GitHubOwner}/{UpdateConfig.GitHubRepo}/releases/latest";

            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(UpdateConfig.HttpTimeoutSeconds);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("BA-BATools-Updater/1.0");

                var token = Environment.GetEnvironmentVariable(UpdateConfig.GitHubTokenEnvVar);
                if (!string.IsNullOrWhiteSpace(token))
                    http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var json = await http.GetStringAsync(url).ConfigureAwait(false);
                return JsonConvert.DeserializeObject<GitHubReleaseInfo>(json);
            }
        }

        public static GitHubAsset? FindAsset(GitHubReleaseInfo rel, string assetName)
        {
            var assets = rel.assets ?? Array.Empty<GitHubAsset>();
            return assets.FirstOrDefault(a => string.Equals(a.name, assetName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Downloads a release asset directly to disk. Writes to a temporary file first and moves
        /// it into place, so an interrupted download never leaves a half written file at the
        /// final path. The installer is a self contained exe and can be over 100 MB, so the
        /// timeout is generous.
        /// </summary>
        public static async Task DownloadAssetToFileAsync(string url, string destinationPath, CancellationToken ct)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(10);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BA-BATools-Updater/1.0");

            var token = Environment.GetEnvironmentVariable(UpdateConfig.GitHubTokenEnvVar);
            if (!string.IsNullOrWhiteSpace(token))
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var tempPath = destinationPath + ".download";

            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            if (File.Exists(destinationPath))
                File.Delete(destinationPath);

            File.Move(tempPath, destinationPath);
        }
    }
}