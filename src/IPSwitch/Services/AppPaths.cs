using System.Diagnostics;
using System.IO;

namespace IPSwitch.Services
{
    /// <summary>
    /// Application paths and the location of user data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// User data (<c>profiles.json</c>, <c>settings.json</c>) is always stored in <c>%AppData%\IPSwitch</c>,
    /// never next to the executable: the <c>.exe</c> can live in a read-only or shared folder and be replaced
    /// by a new version without losing anything.
    /// </para>
    /// <para>
    /// The <see cref="DataFolderOverrideVariable"/> environment variable overrides the folder; tests use it
    /// so they never touch the real user data.
    /// </para>
    /// <para>
    /// Migration: if the data folder has no data yet and an older portable build left an <c>IPSwitch-data</c>
    /// folder next to the executable, its files are copied the first time.
    /// </para>
    /// </remarks>
    public static class AppPaths
    {
        /// <summary>Environment variable that, when set, replaces the data folder (used by tests).</summary>
        public const string DataFolderOverrideVariable = "IPSWITCH_DATA_DIR";

        /// <summary>Full path of the running executable.</summary>
        public static readonly string ExecutablePath =
            Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

        /// <summary>Folder that contains the executable.</summary>
        public static readonly string ExecutableFolder =
            Path.GetDirectoryName(ExecutablePath) ?? AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>Folder used by older portable builds (data next to the executable).</summary>
        private static readonly string LegacyPortableFolder = Path.Combine(ExecutableFolder, "IPSwitch-data");

        private static readonly string[] DataFiles = { "settings.json", "profiles.json" };

        /// <summary>Effective folder where <c>profiles.json</c> and <c>settings.json</c> are stored.</summary>
        public static string DataFolder { get; } = ResolveDataFolder();

        /// <summary>Whether <see cref="DataFolderOverrideVariable"/> redirects the data (tests, development, portable setups).</summary>
        public static bool IsDataFolderOverridden =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DataFolderOverrideVariable));

        /// <summary>Default data folder: <c>%AppData%\IPSwitch</c>.</summary>
        public static string DefaultDataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IPSwitch");

        /// <summary>
        /// <see cref="DataFolder"/> as shown to the user: <c>%AppData%\IPSwitch</c> for the default location,
        /// or the full path when it was overridden.
        /// </summary>
        public static string DisplayDataFolder => FormatForDisplay(DataFolder, DefaultDataFolder);

        /// <summary>Formats a data folder for display (pure function, testable).</summary>
        /// <param name="folder">Effective folder.</param>
        /// <param name="defaultFolder">Default folder (<c>%AppData%\IPSwitch</c> expanded).</param>
        /// <returns><c>%AppData%\IPSwitch</c> if both are the same folder; otherwise <paramref name="folder"/>.</returns>
        public static string FormatForDisplay(string folder, string defaultFolder) =>
            string.Equals(folder.TrimEnd('\\'), defaultFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                ? @"%AppData%\IPSwitch"
                : folder;

        /// <summary>Full path of a file inside <see cref="DataFolder"/>.</summary>
        /// <param name="fileName">File name.</param>
        /// <returns>The full path.</returns>
        public static string GetDataFile(string fileName) => Path.Combine(DataFolder, fileName);

        private static string ResolveDataFolder()
        {
            string? overrideFolder = Environment.GetEnvironmentVariable(DataFolderOverrideVariable);
            string folder = string.IsNullOrWhiteSpace(overrideFolder) ? DefaultDataFolder : overrideFolder!.Trim();

            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppPaths] Could not create {folder}: {ex.Message}");
            }

            MigrateFrom(LegacyPortableFolder, folder);
            return folder;
        }

        /// <summary>
        /// Copies the data files from <paramref name="source"/> when they do not exist yet in <paramref name="target"/>.
        /// </summary>
        /// <param name="source">Data folder of a previous version.</param>
        /// <param name="target">Current data folder.</param>
        /// <remarks>Files are copied, not moved, so the previous version keeps working.</remarks>
        public static void MigrateFrom(string source, string target)
        {
            if (string.Equals(Path.GetFullPath(source).TrimEnd('\\'), Path.GetFullPath(target).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase))
                return;

            foreach (string file in DataFiles)
            {
                try
                {
                    string from = Path.Combine(source, file);
                    string to = Path.Combine(target, file);
                    if (File.Exists(from) && !File.Exists(to))
                        File.Copy(from, to);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AppPaths] Migration of {file} failed: {ex.Message}");
                }
            }
        }
    }
}
