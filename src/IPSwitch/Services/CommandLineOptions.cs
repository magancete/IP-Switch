namespace IPSwitch.Services
{
    /// <summary>
    /// Command-line options supported by <c>IPSwitch.exe</c>.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Argument</term><description>Effect</description></listheader>
    /// <item><term><c>--apply "Name"</c></term><description>Applies the profile and exits (exit code 0 success, 1 error, 2 not found).</description></item>
    /// <item><term><c>--minimized</c></term><description>Starts hidden in the notification area.</description></item>
    /// <item><term><c>--compact</c></term><description>Starts in compact view.</description></item>
    /// <item><term><c>--help</c>, <c>-h</c>, <c>/?</c></term><description>Shows the help and exits.</description></item>
    /// </list>
    /// </remarks>
    public sealed class CommandLineOptions
    {
        /// <summary>Argument to apply a profile.</summary>
        public const string ApplyArgument = "--apply";

        /// <summary>Argument to start hidden in the tray.</summary>
        public const string MinimizedArgument = "--minimized";

        /// <summary>Argument to start in compact view.</summary>
        public const string CompactArgument = "--compact";

        /// <summary>Exit code: profile applied.</summary>
        public const int ExitSuccess = 0;

        /// <summary>Exit code: error while applying, or invalid arguments.</summary>
        public const int ExitError = 1;

        /// <summary>Exit code: there is no profile with that name.</summary>
        public const int ExitProfileNotFound = 2;

        /// <summary>Name of the profile to apply, or <see langword="null"/> if not requested.</summary>
        public string? ApplyProfileName { get; private set; }

        /// <summary>Whether starting hidden in the tray was requested.</summary>
        public bool Minimized { get; private set; }

        /// <summary>Whether starting in compact view was requested.</summary>
        public bool Compact { get; private set; }

        /// <summary>Whether the help was requested.</summary>
        public bool ShowHelp { get; private set; }

        /// <summary>Parse error message, or <see langword="null"/> if the arguments are correct.</summary>
        public string? Error { get; private set; }

        /// <summary>Whether the run is command-line only (no user interface).</summary>
        public bool IsHeadless => ApplyProfileName != null || ShowHelp || Error != null;

        /// <summary>Help text shown with <c>--help</c>.</summary>
        public const string HelpText =
            "IPSwitch.exe [options]\n\n" +
            "  --apply \"Name\"   Apply the profile and exit (0 = ok, 1 = error, 2 = not found)\n" +
            "  --minimized      Start hidden in the notification area\n" +
            "  --compact        Start in compact view\n" +
            "  --help           Show this help";

        /// <summary>Parses the command-line arguments.</summary>
        /// <param name="args">Arguments as received by the process.</param>
        /// <returns>The options; on errors, <see cref="Error"/> holds the description.</returns>
        public static CommandLineOptions Parse(IReadOnlyList<string> args)
        {
            var options = new CommandLineOptions();

            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i];
                switch (arg.ToLowerInvariant())
                {
                    case ApplyArgument:
                        if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]))
                        {
                            options.Error = "--apply requires a profile name.";
                            return options;
                        }
                        options.ApplyProfileName = args[++i].Trim();
                        break;
                    case MinimizedArgument:
                        options.Minimized = true;
                        break;
                    case CompactArgument:
                        options.Compact = true;
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        options.ShowHelp = true;
                        break;
                    default:
                        options.Error = $"Unknown argument: {arg}";
                        return options;
                }
            }

            return options;
        }

        /// <summary>Builds the argument string to apply a profile (quoted when needed).</summary>
        /// <param name="profileName">Profile name.</param>
        /// <returns>Arguments ready for a shortcut or <c>Process.Start</c>.</returns>
        public static string BuildApplyArguments(string profileName) =>
            $"{ApplyArgument} \"{profileName.Replace("\"", string.Empty)}\"";
    }
}
