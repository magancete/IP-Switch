using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Web.Script.Serialization;

namespace IPSwitch.Services
{
    /// <summary>Information about a version published on GitHub.</summary>
    public sealed class UpdateInfo
    {
        /// <summary>Creates the version information.</summary>
        /// <param name="version">Published version number.</param>
        /// <param name="url">Release page to download it.</param>
        /// <param name="executableUrl">Direct download of <c>IPSwitch.exe</c>, if the release has it.</param>
        /// <param name="checksumUrl">Direct download of <c>IPSwitch.exe.sha256</c>, if the release has it.</param>
        public UpdateInfo(Version version, string url, string? executableUrl = null, string? checksumUrl = null)
        {
            Version = version;
            Url = url;
            ExecutableUrl = executableUrl;
            ChecksumUrl = checksumUrl;
        }

        /// <summary>Published version number.</summary>
        public Version Version { get; }

        /// <summary>URL of the release page.</summary>
        public string Url { get; }

        /// <summary>Direct download of the executable, or <see langword="null"/>.</summary>
        public string? ExecutableUrl { get; }

        /// <summary>Direct download of the SHA-256 checksum file, or <see langword="null"/>.</summary>
        public string? ChecksumUrl { get; }

        /// <summary>
        /// Whether the app can install this version by itself (<see cref="SelfUpdater"/>): the release has both the
        /// executable and its checksum, which is always verified before replacing anything.
        /// </summary>
        public bool CanInstall => ExecutableUrl != null && ChecksumUrl != null;
    }

    /// <summary>
    /// Checks whether a newer version has been published in the GitHub releases.
    /// </summary>
    /// <remarks>
    /// It queries <c>https://api.github.com/repos/magancete/IP-Switch/releases/latest</c> (or the URL in
    /// <see cref="FeedOverrideVariable"/>, used to test updates locally). Installing is done by
    /// <see cref="SelfUpdater"/>, only when the user asks for it.
    /// </remarks>
    public static class UpdateService
    {
        /// <summary>GitHub API endpoint with the latest release.</summary>
        public const string LatestReleaseApi = "https://api.github.com/repos/magancete/IP-Switch/releases/latest";

        /// <summary>Public releases page.</summary>
        public const string ReleasesPage = "https://github.com/magancete/IP-Switch/releases";

        /// <summary>Environment variable that replaces <see cref="LatestReleaseApi"/> (tests of the update flow).</summary>
        public const string FeedOverrideVariable = "IPSWITCH_UPDATE_FEED";

        /// <summary>Name of the executable asset in every release.</summary>
        public const string ExecutableAsset = "IPSwitch.exe";

        /// <summary>Name of the checksum asset in every release.</summary>
        public const string ChecksumAsset = "IPSwitch.exe.sha256";

        /// <summary>Version of the running application (major.minor.build).</summary>
        public static Version CurrentVersion
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        /// <summary>Endpoint queried for the latest release.</summary>
        public static string FeedUrl =>
            Environment.GetEnvironmentVariable(FeedOverrideVariable) is { Length: > 0 } custom ? custom : LatestReleaseApi;

        /// <summary>Queries the latest published release.</summary>
        /// <returns>
        /// The release information if it is newer than <see cref="CurrentVersion"/>; <see langword="null"/> if there is
        /// no newer version or it could not be checked (offline, API rate limit, etc.).
        /// </returns>
        public static async Task<UpdateInfo?> CheckAsync()
        {
            try
            {
                string json = await DownloadStringAsync(FeedUrl).ConfigureAwait(false);
                return ParseRelease(json, CurrentVersion);
            }
            catch (Exception ex) when (ex is WebException or IOException or InvalidOperationException or ArgumentException)
            {
                Debug.WriteLine($"[UpdateService] {ex.Message}");
                return null;
            }
        }

        /// <summary>Reads a GitHub "latest release" response (pure function, testable).</summary>
        /// <param name="json">Response body.</param>
        /// <param name="current">Running version.</param>
        /// <returns>The release if it is newer than <paramref name="current"/>; otherwise <see langword="null"/>.</returns>
        public static UpdateInfo? ParseRelease(string json, Version current)
        {
            var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            if (data == null ||
                !data.TryGetValue("tag_name", out var tag) ||
                !TryParseVersion(tag as string, out var latest) ||
                !IsNewer(latest, current))
                return null;

            string url = data.TryGetValue("html_url", out var htmlUrl) && htmlUrl is string s ? s : ReleasesPage;
            // JavaScriptSerializer returns JSON arrays as ArrayList (or object[]): accept any collection
            var assets = (data.TryGetValue("assets", out var list) ? list as System.Collections.IEnumerable : null)?
                .OfType<Dictionary<string, object>>()
                .ToList() ?? new List<Dictionary<string, object>>();

            string? AssetUrl(string name) => assets
                .Where(a => a.TryGetValue("name", out var n) && string.Equals(n as string, name, StringComparison.OrdinalIgnoreCase))
                .Select(a => a.TryGetValue("browser_download_url", out var u) ? u as string : null)
                .FirstOrDefault(u => !string.IsNullOrEmpty(u));

            return new UpdateInfo(latest, url, AssetUrl(ExecutableAsset), AssetUrl(ChecksumAsset));
        }

        /// <summary>Downloads a small text resource (API response, checksum file).</summary>
        /// <param name="url">Address.</param>
        /// <returns>The body as text.</returns>
        internal static async Task<string> DownloadStringAsync(string url)
        {
            using var response = await CreateRequest(url, 8000).GetResponseAsync().ConfigureAwait(false);
            using var reader = new StreamReader(response.GetResponseStream()!);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        /// <summary>Creates a GitHub-friendly request (TLS 1.2, user agent, timeout).</summary>
        /// <param name="url">Address.</param>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <returns>The request.</returns>
        internal static HttpWebRequest CreateRequest(string url, int timeoutMs)
        {
            // .NET Framework 4.8 does not enable TLS 1.2 by default on every system; GitHub requires it
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "IPSwitch/" + CurrentVersion;
            request.Accept = "application/vnd.github+json, application/octet-stream, */*";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            return request;
        }

        /// <summary>Parses a version tag such as <c>v1.2.3</c> or <c>1.2</c>.</summary>
        /// <param name="tag">Release tag.</param>
        /// <param name="version">Resulting version (major.minor.build).</param>
        /// <returns><see langword="true"/> if the tag is valid.</returns>
        public static bool TryParseVersion(string? tag, out Version version)
        {
            version = new Version(0, 0, 0);
            if (string.IsNullOrWhiteSpace(tag)) return false;

            string text = tag!.Trim().TrimStart('v', 'V');
            int suffix = text.IndexOfAny(new[] { '-', '+' });
            if (suffix >= 0) text = text.Substring(0, suffix);

            if (!Version.TryParse(text.Contains(".") ? text : text + ".0", out var parsed)) return false;

            version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
            return true;
        }

        /// <summary>Whether <paramref name="candidate"/> is later than <paramref name="current"/>.</summary>
        /// <param name="candidate">Published version.</param>
        /// <param name="current">Running version.</param>
        /// <returns><see langword="true"/> if there is an update.</returns>
        public static bool IsNewer(Version candidate, Version current) => candidate > current;
    }
}
