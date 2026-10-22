using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace IPSwitch.Services
{
    /// <summary>
    /// Administrator rights: the executable starts without them (<c>asInvoker</c> manifest) and elevates only when
    /// there is no running instance to hand the request to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With a <c>requireAdministrator</c> manifest Windows shows the UAC prompt before the process even starts, so
    /// opening the <c>.exe</c> again while the app is in the tray would prompt again just to bring it to the front.
    /// Instead, an unelevated launch first talks to the running (elevated) instance through
    /// <see cref="SingleInstance"/>; only if none is running does it restart itself elevated (one UAC prompt).
    /// </para>
    /// <para>
    /// For development, the <see cref="SkipElevationVariable"/> environment variable keeps the app unelevated
    /// (the UI works; network changes fail).
    /// </para>
    /// </remarks>
    public static class Elevation
    {
        /// <summary>Environment variable that, set to <c>1</c>, disables the automatic elevation (UI tests).</summary>
        public const string SkipElevationVariable = "IPSWITCH_NO_ELEVATION";

        /// <summary>Win32 error returned when the user cancels the UAC prompt.</summary>
        private const int ErrorCancelled = 1223;

        /// <summary>Whether the process runs with administrator rights.</summary>
        public static bool IsElevated { get; } = ComputeIsElevated();

        /// <summary>Whether the automatic elevation is disabled for this process.</summary>
        public static bool IsSkipped =>
            Environment.GetEnvironmentVariable(SkipElevationVariable) == "1";

        /// <summary>Starts this executable again with administrator rights (UAC prompt).</summary>
        /// <param name="arguments">Original command-line arguments.</param>
        /// <returns>The elevated process, or <see langword="null"/> if the user cancelled the prompt or it failed.</returns>
        public static Process? RestartElevated(IEnumerable<string> arguments)
        {
            try
            {
                return Process.Start(new ProcessStartInfo(AppPaths.ExecutablePath, JoinArguments(arguments))
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != ErrorCancelled)
                    Debug.WriteLine($"[Elevation] {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Joins arguments into a command line that Windows splits back into the same arguments
        /// (<c>CommandLineToArgvW</c> rules). Pure function, testable.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <returns>The command line.</returns>
        public static string JoinArguments(IEnumerable<string> arguments) =>
            string.Join(" ", arguments.Select(QuoteArgument));

        private static string QuoteArgument(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
                return argument;

            var builder = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                // Backslashes before a quote are doubled, and the quote itself is escaped
                builder.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
                backslashes = 0;
                builder.Append(c);
            }

            // Backslashes before the closing quote are doubled
            builder.Append('\\', backslashes * 2).Append('"');
            return builder.ToString();
        }

        private static bool ComputeIsElevated()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
