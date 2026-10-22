using IPSwitch.Models;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace IPSwitch.Services
{
    /// <summary>
    /// Creates desktop shortcuts (<c>.lnk</c>) that apply a profile through the command line, and repairs them when the
    /// executable is moved.
    /// </summary>
    /// <remarks>
    /// Uses the <c>WScript.Shell</c> COM object, available in every Windows version, to avoid dependencies.
    /// The shortcut runs <c>IPSwitch.exe --apply "Name"</c>. If IP Switch is already running, the request is forwarded
    /// to it without a UAC prompt (see <see cref="Elevation"/> and <see cref="SingleInstance"/>).
    /// </remarks>
    public static class ShortcutService
    {
        /// <summary>Creates (or overwrites) a shortcut on the user's desktop that applies a profile.</summary>
        /// <param name="profile">Profile the shortcut applies.</param>
        /// <returns>Full path of the created <c>.lnk</c> file.</returns>
        /// <exception cref="InvalidOperationException">If the Windows Script Host COM object is not available.</exception>
        public static string CreateDesktopShortcut(IPProfile profile)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string path = Path.Combine(desktop, $"IP Switch - {SanitizeFileName(profile.Name)}.lnk");

            WithShortcut(path, link =>
            {
                link.Set("Arguments", CommandLineOptions.BuildApplyArguments(profile.Name));
                link.Set("Description", $"IP Switch: {profile.Name}");
                PointTo(link, AppPaths.ExecutablePath);
                link.Save();
            });
            return path;
        }

        /// <summary>
        /// Points the desktop shortcuts created by IP Switch back to this executable when the one they used no longer
        /// exists (the <c>.exe</c> was moved or renamed). Shortcuts to another copy that still exists are left alone.
        /// </summary>
        /// <returns>Number of repaired shortcuts.</returns>
        /// <remarks>Reads every <c>.lnk</c> on the desktop: call it from a background thread.</remarks>
        public static int RepairDesktopShortcuts() =>
            RepairShortcuts(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppPaths.ExecutablePath);

        /// <summary>Repairs the IP Switch shortcuts of a folder (see <see cref="RepairDesktopShortcuts"/>).</summary>
        /// <param name="folder">Folder with <c>.lnk</c> files.</param>
        /// <param name="executable">Executable the broken shortcuts must point to.</param>
        /// <returns>Number of repaired shortcuts.</returns>
        public static int RepairShortcuts(string folder, string executable)
        {
            int repaired = 0;
            if (!Directory.Exists(folder)) return 0;

            foreach (string path in Directory.EnumerateFiles(folder, "*.lnk"))
            {
                try
                {
                    WithShortcut(path, link =>
                    {
                        string target = link.Get("TargetPath");
                        if (!NeedsRepair(target, link.Get("Arguments"), executable, File.Exists(target)))
                            return;

                        PointTo(link, executable);
                        link.Save();
                        repaired++;
                    });
                }
                catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or TargetInvocationException)
                {
                    // An unreadable or locked shortcut is skipped; the others are still repaired
                    Debug.WriteLine($"[ShortcutService] {path}: {ex.Message}");
                }
            }
            return repaired;
        }

        /// <summary>
        /// Whether a shortcut is an IP Switch "apply profile" shortcut whose executable is gone (pure function, testable).
        /// </summary>
        /// <param name="target">Executable the shortcut runs.</param>
        /// <param name="arguments">Its arguments.</param>
        /// <param name="currentExecutable">Path of the running executable.</param>
        /// <param name="targetExists">Whether <paramref name="target"/> still exists.</param>
        /// <returns><see langword="true"/> if it must be pointed to <paramref name="currentExecutable"/>.</returns>
        public static bool NeedsRepair(string? target, string? arguments, string currentExecutable, bool targetExists)
        {
            if (string.IsNullOrWhiteSpace(target) || targetExists) return false;

            bool isIpSwitch = string.Equals(Path.GetFileName(target), Path.GetFileName(currentExecutable), StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(Path.GetFileName(target), "IPSwitch.exe", StringComparison.OrdinalIgnoreCase);
            bool appliesProfile = (arguments ?? string.Empty).TrimStart()
                .StartsWith(CommandLineOptions.ApplyArgument, StringComparison.OrdinalIgnoreCase);

            return isIpSwitch && appliesProfile &&
                   !string.Equals(target, currentExecutable, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Replaces the characters that are invalid in file names with <c>_</c>.</summary>
        /// <param name="name">Original name.</param>
        /// <returns>A name valid for the file system.</returns>
        public static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string sanitized = new(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "Profile" : sanitized.Trim();
        }

        private static void PointTo(ShortcutLink link, string executable)
        {
            link.Set("TargetPath", executable);
            link.Set("WorkingDirectory", Path.GetDirectoryName(executable) ?? string.Empty);
            link.Set("IconLocation", executable + ",0");
        }

        /// <summary>Opens (or creates, if it does not exist) a <c>.lnk</c> through <c>WScript.Shell</c> and releases the COM objects.</summary>
        private static void WithShortcut(string path, Action<ShortcutLink> action)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                            ?? throw new InvalidOperationException("WScript.Shell is not available.");

            object? shell = null, link = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                action(new ShortcutLink(link!));
            }
            finally
            {
                if (link != null) Marshal.FinalReleaseComObject(link);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }

        /// <summary>Late-bound access to a <c>WshShortcut</c> COM object.</summary>
        private sealed class ShortcutLink
        {
            private readonly object _link;
            private readonly Type _type;

            public ShortcutLink(object link)
            {
                _link = link;
                _type = link.GetType();
            }

            public string Get(string property) =>
                _type.InvokeMember(property, BindingFlags.GetProperty, null, _link, null) as string ?? string.Empty;

            public void Set(string property, object value) =>
                _type.InvokeMember(property, BindingFlags.SetProperty, null, _link, new[] { value });

            public void Save() => _type.InvokeMember("Save", BindingFlags.InvokeMethod, null, _link, null);
        }
    }
}
