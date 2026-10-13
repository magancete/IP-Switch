using System.Diagnostics;

namespace IPSwitch.Services
{
    /// <summary>
    /// Manages starting with Windows through a scheduled task with elevated privileges.
    /// </summary>
    /// <remarks>
    /// The <c>HKCU\...\Run</c> registry key cannot be used: Windows does not start applications that require
    /// administrator rights from it at sign-in. The <c>IPSwitch</c> task is created with
    /// <c>schtasks /SC ONLOGON /RL HIGHEST</c> and starts the executable with <see cref="CommandLineOptions.MinimizedArgument"/>.
    /// If the <c>.exe</c> is moved, the option must be turned on again to update the path.
    /// Each call runs <c>schtasks</c> (tens of milliseconds): call it from a background thread.
    /// </remarks>
    public static class StartupService
    {
        private const string TaskName = "IPSwitch";

        /// <summary>Whether the startup scheduled task exists.</summary>
        /// <returns><see langword="true"/> if the task exists.</returns>
        public static bool IsEnabled() => RunSchtasks($"/Query /TN \"{TaskName}\"");

        /// <summary>Creates or deletes the startup scheduled task.</summary>
        /// <param name="enabled"><see langword="true"/> to create (or update) it, <see langword="false"/> to delete it.</param>
        /// <returns><see langword="true"/> if the operation succeeded.</returns>
        public static bool SetEnabled(bool enabled)
        {
            if (!enabled)
                return !IsEnabled() || RunSchtasks($"/Delete /TN \"{TaskName}\" /F");

            // /TR needs the inner quotes escaped as \"
            string command = $"\\\"{AppPaths.ExecutablePath}\\\" {CommandLineOptions.MinimizedArgument}";
            return RunSchtasks($"/Create /TN \"{TaskName}\" /TR \"{command}\" /SC ONLOGON /RL HIGHEST /F");
        }

        private static bool RunSchtasks(string arguments)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                if (process == null) return false;
                var errorTask = process.StandardError.ReadToEndAsync();
                process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                _ = errorTask.Result;
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartupService] {ex.Message}");
                return false;
            }
        }
    }
}
