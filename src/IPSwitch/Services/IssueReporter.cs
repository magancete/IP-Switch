using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace IPSwitch.Services
{
    /// <summary>
    /// Sends the user to the GitHub issue chooser, where they pick the kind of report
    /// (bug, improvement, documentation or question) defined in <c>.github/ISSUE_TEMPLATE</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A single entry point keeps the app simple; the categories and their labels live in the repository,
    /// so they can change without a new release.
    /// </para>
    /// <para>
    /// The chooser page cannot receive prefilled fields, so a short environment summary (app version, Windows
    /// version and language) is copied to the clipboard to paste into the form. It <b>never</b> includes IP
    /// addresses, adapter names or profiles. Nothing is published automatically.
    /// </para>
    /// </remarks>
    public static class IssueReporter
    {
        /// <summary>GitHub page where the user chooses the issue template.</summary>
        public const string ChooseUrl = "https://github.com/magancete/IP-Switch/issues/new/choose";

        /// <summary>Environment data useful in a report.</summary>
        /// <returns>App version (<c>version</c>), Windows version (<c>os</c>) and language (<c>language</c>).</returns>
        public static Dictionary<string, string> GetEnvironmentFields() => new()
        {
            ["version"] = UpdateService.CurrentVersion.ToString(),
            ["os"] = GetWindowsVersion(),
            ["language"] = Loc.CurrentLanguage
        };

        /// <summary>Builds the one-line summary copied to the clipboard.</summary>
        /// <param name="fields">Fields returned by <see cref="GetEnvironmentFields"/>.</param>
        /// <returns>For example <c>IP Switch 1.0.0 · Windows 11 Pro 24H2 (26100.1) · Language: en</c>.</returns>
        public static string BuildSummary(IReadOnlyDictionary<string, string> fields)
        {
            string Get(string key) => fields.TryGetValue(key, out string? value) ? value : string.Empty;
            return $"IP Switch {Get("version")} · {Get("os")} · Language: {Get("language")}";
        }

        /// <summary>Copies the environment summary to the clipboard and opens the issue chooser.</summary>
        /// <returns><see langword="true"/> if the browser could be opened.</returns>
        public static bool Open()
        {
            try
            {
                Clipboard.SetText(BuildSummary(GetEnvironmentFields()));
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
            {
                // The clipboard may be locked by another process: the report can still be written by hand
                Debug.WriteLine($"[IssueReporter] Clipboard: {ex.Message}");
            }

            try
            {
                Process.Start(new ProcessStartInfo(ChooseUrl) { UseShellExecute = true });
                return true;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Debug.WriteLine($"[IssueReporter] {ex.Message}");
                return false;
            }
        }

        /// <summary>Readable Windows version, e.g. <c>Windows 11 Pro 23H2 (22631.4317)</c>.</summary>
        /// <returns>The description, or <see cref="Environment.OSVersion"/> if the registry cannot be read.</returns>
        public static string GetWindowsVersion()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                string product = key?.GetValue("ProductName") as string ?? "Windows";
                string display = key?.GetValue("DisplayVersion") as string ?? string.Empty;
                string build = key?.GetValue("CurrentBuild") as string ?? string.Empty;
                string ubr = key?.GetValue("UBR")?.ToString() ?? string.Empty;

                // Windows 11 still reports "Windows 10" in ProductName: corrected by build number
                if (int.TryParse(build, out int buildNumber) && buildNumber >= 22000)
                    product = product.Replace("Windows 10", "Windows 11");

                string buildText = string.IsNullOrEmpty(ubr) ? build : $"{build}.{ubr}";
                return $"{product} {display} ({buildText})".Replace("  ", " ").Trim();
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return Environment.OSVersion.VersionString;
            }
        }
    }
}
