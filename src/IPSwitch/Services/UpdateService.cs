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
        public UpdateInfo(Version version, string url)
        {
            Version = version;
            Url = url;
        }

        /// <summary>Published version number.</summary>
        public Version Version { get; }

        /// <summary>URL of the release page.</summary>
        public string Url { get; }
    }

    /// <summary>
    /// Checks whether a newer version has been published in the GitHub releases.
    /// </summary>
    /// <remarks>
    /// It only checks and notifies; it never downloads or installs anything automatically.
    /// It queries <c>https://api.github.com/repos/magancete/IP-Switch/releases/latest</c>.
    /// </remarks>
    public static class UpdateService
    {
        /// <summary>GitHub API endpoint with the latest release.</summary>
        public const string LatestReleaseApi = "https://api.github.com/repos/magancete/IP-Switch/releases/latest";

        /// <summary>Public releases page.</summary>
        public const string ReleasesPage = "https://github.com/magancete/IP-Switch/releases";

        /// <summary>Version of the running application (major.minor.build).</summary>
        public static Version CurrentVersion
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        /// <summary>Queries the latest published release.</summary>
        /// <returns>
        /// The release information if it is newer than <see cref="CurrentVersion"/>; <see langword="null"/> if there is
        /// no newer version or it could not be checked (offline, API rate limit, etc.).
        /// </returns>
        public static async Task<UpdateInfo?> CheckAsync()
        {
            try
            {
                // .NET Framework 4.8 does not enable TLS 1.2 by default on every system; GitHub requires it
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                var request = (HttpWebRequest)WebRequest.Create(LatestReleaseApi);
                request.UserAgent = "IPSwitch/" + CurrentVersion;
                request.Accept = "application/vnd.github+json";
                request.Timeout = 8000;

                using var response = await request.GetResponseAsync().ConfigureAwait(false);
                using var reader = new StreamReader(response.GetResponseStream()!);
                string json = await reader.ReadToEndAsync().ConfigureAwait(false);

                var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (data == null ||
                    !data.TryGetValue("tag_name", out var tag) ||
                    !TryParseVersion(tag as string, out var latest))
                    return null;

                string url = data.TryGetValue("html_url", out var htmlUrl) && htmlUrl is string s ? s : ReleasesPage;
                return IsNewer(latest, CurrentVersion) ? new UpdateInfo(latest, url) : null;
            }
            catch (Exception ex) when (ex is WebException or IOException or InvalidOperationException or ArgumentException)
            {
                Debug.WriteLine($"[UpdateService] {ex.Message}");
                return null;
            }
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
