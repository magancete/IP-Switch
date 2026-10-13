using IPSwitch.Models;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace IPSwitch.Services
{
    /// <summary>
    /// Creates desktop shortcuts (<c>.lnk</c>) that apply a profile through the command line.
    /// </summary>
    /// <remarks>
    /// Uses the <c>WScript.Shell</c> COM object, available in every Windows version, to avoid dependencies.
    /// The shortcut runs <c>IPSwitch.exe --apply "Name"</c>; Windows shows the UAC prompt when it is used. If IP Switch
    /// is already running, the request is forwarded to it (see <see cref="SingleInstance"/>).
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

            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                            ?? throw new InvalidOperationException("WScript.Shell is not available.");

            object? shell = null, link = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });

                var linkType = link!.GetType();
                void Set(string property, object value) =>
                    linkType.InvokeMember(property, BindingFlags.SetProperty, null, link, new[] { value });

                Set("TargetPath", AppPaths.ExecutablePath);
                Set("Arguments", CommandLineOptions.BuildApplyArguments(profile.Name));
                Set("WorkingDirectory", AppPaths.ExecutableFolder);
                Set("IconLocation", AppPaths.ExecutablePath + ",0");
                Set("Description", $"IP Switch: {profile.Name}");
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);

                return path;
            }
            finally
            {
                if (link != null) Marshal.FinalReleaseComObject(link);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
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
    }
}
