using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace IPSwitch.Services
{
    /// <summary>
    /// Manages starting with Windows through a scheduled task with elevated privileges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>HKCU\...\Run</c> registry key cannot be used: Windows does not start elevated applications from it at
    /// sign-in. The <c>IPSwitch</c> task is registered from a full XML definition (<see cref="BuildTaskXml"/>) instead
    /// of <c>schtasks /SC ONLOGON</c>, whose defaults break a resident tray app: Windows would stop it after 72 hours
    /// and would not start it on battery power.
    /// </para>
    /// <para>
    /// The user's choice is stored in <see cref="Models.AppSettings.StartWithWindows"/>; <see cref="Synchronize"/>
    /// recreates the task when it is missing or points to another copy of the executable (moved or updated).
    /// Every call runs <c>schtasks</c> (tens of milliseconds): call it from a background thread.
    /// </para>
    /// </remarks>
    public static class StartupService
    {
        /// <summary>Name of the scheduled task.</summary>
        public const string TaskName = "IPSwitch";

        /// <summary>Delay after sign-in before the app starts, so the desktop and the tray are ready.</summary>
        private const string LogonDelay = "PT10S";

        private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        /// <summary>Executable started by the task.</summary>
        /// <returns>The full path, or <see langword="null"/> if the task does not exist.</returns>
        public static string? GetTaskExecutable()
        {
            var result = RunSchtasks($"/Query /TN \"{TaskName}\" /XML");
            return result.Success ? ParseTaskCommand(result.Output) : null;
        }

        /// <summary>Whether the startup task exists.</summary>
        /// <returns><see langword="true"/> if the task exists.</returns>
        public static bool IsEnabled() => GetTaskExecutable() != null;

        /// <summary>Creates (or updates) or deletes the startup task.</summary>
        /// <param name="enabled"><see langword="true"/> to register it for this executable, <see langword="false"/> to delete it.</param>
        /// <returns>The result; on failure, the <c>schtasks</c> error message.</returns>
        public static CommandResult SetEnabled(bool enabled)
        {
            if (!enabled)
            {
                if (GetTaskExecutable() == null) return CommandResult.Ok;
                var deleted = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
                return deleted.Success ? CommandResult.Ok : new CommandResult(false, deleted.Output);
            }

            string file = Path.Combine(Path.GetTempPath(), $"IPSwitch-task-{Guid.NewGuid():N}.xml");
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                    // schtasks /XML requires a UTF-16 file
                    File.WriteAllText(file, BuildTaskXml(AppPaths.ExecutablePath, identity.User!.Value), Encoding.Unicode);

                var created = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F");
                return created.Success ? CommandResult.Ok : new CommandResult(false, created.Output);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                return new CommandResult(false, ex.Message);
            }
            finally
            {
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>
        /// Makes the task match the setting: recreates it when it is wanted but missing or registered for another
        /// executable path. A task left by a version without the setting turns the setting on.
        /// </summary>
        /// <param name="wanted">Value of <see cref="Models.AppSettings.StartWithWindows"/>.</param>
        /// <returns>The value the setting must have afterwards.</returns>
        public static bool Synchronize(bool wanted)
        {
            string? registered = GetTaskExecutable();
            if (!wanted) return registered != null;

            if (!PathsEqual(registered, AppPaths.ExecutablePath))
                SetEnabled(true);
            return true;
        }

        /// <summary>Builds the task definition (pure function, testable).</summary>
        /// <param name="executablePath">Executable to start.</param>
        /// <param name="userSid">SID of the user whose sign-in starts the task.</param>
        /// <returns>The task XML (Task Scheduler schema 1.2).</returns>
        public static string BuildTaskXml(string executablePath, string userSid)
        {
            XNamespace ns = TaskNamespace;
            var task = new XElement(ns + "Task", new XAttribute("version", "1.2"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", "Starts IP Switch in the notification area when you sign in."),
                    new XElement(ns + "URI", @"\" + TaskName)),
                new XElement(ns + "Triggers",
                    new XElement(ns + "LogonTrigger",
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "UserId", userSid),
                        new XElement(ns + "Delay", LogonDelay))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal", new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", userSid),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "AllowHardTerminate", "true"),
                    new XElement(ns + "StartWhenAvailable", "false"),
                    new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(ns + "IdleSettings",
                        new XElement(ns + "StopOnIdleEnd", "false"),
                        new XElement(ns + "RestartOnIdle", "false")),
                    new XElement(ns + "AllowStartOnDemand", "true"),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "Hidden", "false"),
                    new XElement(ns + "RunOnlyIfIdle", "false"),
                    // PT0S = no time limit (the default, PT72H, would close the tray app after three days)
                    new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                    // Normal priority: tasks start below normal by default, which makes the UI sluggish
                    new XElement(ns + "Priority", "5")),
                new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", executablePath),
                        new XElement(ns + "Arguments", CommandLineOptions.MinimizedArgument))));

            return new XDeclaration("1.0", "UTF-16", null) + Environment.NewLine + task;
        }

        /// <summary>Reads the executable of the first action of a task definition (pure function, testable).</summary>
        /// <param name="taskXml">Output of <c>schtasks /Query /XML</c>.</param>
        /// <returns>The executable path without quotes, or <see langword="null"/> if it cannot be read.</returns>
        public static string? ParseTaskCommand(string taskXml)
        {
            try
            {
                var command = XDocument.Parse(taskXml).Descendants(TaskNamespace + "Command").FirstOrDefault()?.Value;
                return string.IsNullOrWhiteSpace(command) ? null : command!.Trim().Trim('"');
            }
            catch (System.Xml.XmlException)
            {
                return null;
            }
        }

        private static bool PathsEqual(string? a, string b) =>
            a != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        private static (bool Success, string Output) RunSchtasks(string arguments)
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
                    RedirectStandardError = true,
                    // Console tools write using the OEM code page (paths with accents, localized errors)
                    StandardOutputEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                    StandardErrorEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                });

                if (process == null) return (false, "schtasks could not be started.");
                var errorTask = process.StandardError.ReadToEndAsync();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                string error = errorTask.Result;
                return process.ExitCode == 0 ? (true, output) : (false, (error + output).Trim());
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Debug.WriteLine($"[StartupService] {ex.Message}");
                return (false, ex.Message);
            }
        }
    }
}
