using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace IPSwitch.Services
{
    /// <summary>
    /// Installs a newer release in place: downloads <c>IPSwitch.exe</c>, verifies its SHA-256 and swaps it for the
    /// running executable.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item><description>
    /// The new executable is downloaded next to the current one (<c>IPSwitch.exe.download</c>), so the swap is a
    /// rename on the same volume.
    /// </description></item>
    /// <item><description>
    /// Its SHA-256 must match the release's <c>IPSwitch.exe.sha256</c>; otherwise it is deleted and nothing changes.
    /// </description></item>
    /// <item><description>
    /// Windows does not allow overwriting a running executable but does allow renaming it: the current file becomes
    /// <c>IPSwitch.exe.old</c> and the download takes its name. If that fails, the original name is restored.
    /// </description></item>
    /// <item><description>
    /// The caller then restarts the app; the new version deletes the <c>.old</c> file
    /// (<see cref="CleanUpPreviousVersion"/>).
    /// </description></item>
    /// </list>
    /// The app runs elevated, so the executable folder is writable even under <c>Program Files</c>.
    /// </remarks>
    public static class SelfUpdater
    {
        /// <summary>Suffix of the previous executable kept until the new version starts.</summary>
        public const string PreviousSuffix = ".old";

        private const string DownloadSuffix = ".download";

        /// <summary>Downloads, verifies and swaps in the new executable.</summary>
        /// <param name="update">Release to install (<see cref="UpdateInfo.CanInstall"/> must be true).</param>
        /// <returns>The result; on failure, a localized explanation.</returns>
        /// <remarks>Call it on the UI thread (localized texts are read before the first await).</remarks>
        public static async Task<CommandResult> InstallAsync(UpdateInfo update)
        {
            if (!update.CanInstall)
                return new CommandResult(false, Loc.Get("Update_NoAsset"));
            string checksumMismatch = Loc.Get("Update_ChecksumMismatch");

            string current = AppPaths.ExecutablePath;
            string downloaded = current + DownloadSuffix;
            string previous = current + PreviousSuffix;

            try
            {
                await DownloadFileAsync(update.ExecutableUrl!, downloaded).ConfigureAwait(false);

                string? expected = ParseChecksum(await UpdateService.DownloadStringAsync(update.ChecksumUrl!).ConfigureAwait(false));
                string actual = await Task.Run(() => ComputeSha256(downloaded)).ConfigureAwait(false);
                if (expected == null || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    return new CommandResult(false, checksumMismatch);

                if (File.Exists(previous)) File.Delete(previous);
                File.Move(current, previous);
                try
                {
                    File.Move(downloaded, current);
                }
                catch
                {
                    // Put the running version back under its name so the app keeps working
                    File.Move(previous, current);
                    throw;
                }

                return CommandResult.Ok;
            }
            catch (Exception ex) when (ex is WebException or IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[SelfUpdater] {ex.Message}");
                return new CommandResult(false, ex.Message);
            }
            finally
            {
                TryDelete(downloaded);
            }
        }

        /// <summary>
        /// Deletes the executable replaced by the last update. The previous process may still be closing, so it retries
        /// for a few seconds in the background.
        /// </summary>
        public static void CleanUpPreviousVersion()
        {
            string previous = AppPaths.ExecutablePath + PreviousSuffix;
            if (!File.Exists(previous)) return;

            Task.Run(async () =>
            {
                for (int attempt = 0; attempt < 10 && File.Exists(previous); attempt++)
                {
                    if (TryDelete(previous)) return;
                    await Task.Delay(1000).ConfigureAwait(false);
                }
            });
        }

        /// <summary>
        /// Extracts the hash from a checksum file (<c>sha256sum</c> format: <c>hash  file name</c>, or the bare hash).
        /// Pure function, testable.
        /// </summary>
        /// <param name="text">File content.</param>
        /// <returns>The 64-character hexadecimal hash in lower case, or <see langword="null"/> if there is none.</returns>
        public static string? ParseChecksum(string? text)
        {
            var match = Regex.Match(text ?? string.Empty, @"\b[0-9a-fA-F]{64}\b");
            return match.Success ? match.Value.ToLowerInvariant() : null;
        }

        /// <summary>SHA-256 of a file as lower-case hexadecimal.</summary>
        /// <param name="path">File.</param>
        /// <returns>The hash.</returns>
        public static string ComputeSha256(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static async Task DownloadFileAsync(string url, string path)
        {
            using var response = await UpdateService.CreateRequest(url, 60000).GetResponseAsync().ConfigureAwait(false);
            using var source = response.GetResponseStream()!;
            using var target = File.Create(path);
            await source.CopyToAsync(target).ConfigureAwait(false);
        }

        private static bool TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
