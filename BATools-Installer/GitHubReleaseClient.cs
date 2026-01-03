// File: BATools-Installer/GitHubReleaseClient.cs
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace BATools_Installer
{
    internal sealed class GitHubReleaseClient
    {
        private readonly string _owner;
        private readonly string _repo;

        public GitHubReleaseClient(string owner, string repo)
        {
            _owner = owner;
            _repo = repo;
        }

        public async Task<string> DownloadLatestAssetToTempAsync(string assetName)
        {
            using var http = CreateHttpClient();

            var api = $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";
            using var resp = await http.GetAsync(api).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var assets = doc.RootElement.GetProperty("assets");
            string? url = null;

            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString();
                if (string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase))
                {
                    url = a.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException($"Asset '{assetName}' not found in latest release.");

            var temp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{assetName}");

            using var fileResp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            fileResp.EnsureSuccessStatusCode();

            await using var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
            await fileResp.Content.CopyToAsync(fs).ConfigureAwait(false);

            return temp;
        }

        public async Task<string> DownloadAssetUrlToTempAsync(string assetUrl, string fileNameHint, Action<string>? log = null)
        {
            if (string.IsNullOrWhiteSpace(assetUrl))
                throw new ArgumentException("assetUrl is empty.");

            var temp = Path.Combine(Path.GetTempPath(), fileNameHint);
            log?.Invoke("Downloading asset: " + assetUrl);

            using var http = CreateHttpClient();

            using var resp = await http.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            await using var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
            await resp.Content.CopyToAsync(fs).ConfigureAwait(false);

            log?.Invoke("Downloaded to: " + temp);
            return temp;
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All
            };

            var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(5)
            };

            http.DefaultRequestHeaders.UserAgent.Clear();
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BATools-Installer", "1.0"));

            // GitHub API likes this; harmless for browser_download_url too
            http.DefaultRequestHeaders.Accept.Clear();
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var token = GetToken();
            if (!string.IsNullOrWhiteSpace(token))
            {
                // Modern GitHub: "Bearer" for fine-grained tokens
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return http;
        }

        private static string? GetToken()
        {
            // Works even if public: returns null and we just don't attach auth header.
            var token = Environment.GetEnvironmentVariable(InstallerConfig.GitHubTokenEnvVar);
            return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        }
    }
}
