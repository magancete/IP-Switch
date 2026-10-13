using IPSwitch.Models;
using IPSwitch.Services;
using IPSwitch.Windows;
using System.Windows;
using Wpf.Ui.Appearance;
using Forms = System.Windows.Forms;

namespace IPSwitch
{
    /// <summary>
    /// Application entry point.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>Parses the command line (<see cref="CommandLineOptions"/>).</description></item>
    /// <item><description>
    /// Guarantees a single process on the machine (<see cref="SingleInstance"/>): a later launch asks the running
    /// instance to show itself or to apply a profile (<c>--apply</c>) and exits with its result.
    /// </description></item>
    /// <item><description>Applies language and theme before creating the windows and restores the last view (full or compact).</description></item>
    /// </list>
    /// The application only shuts down explicitly (<c>ShutdownMode=OnExplicitShutdown</c>) because it can keep running in the tray.
    /// </remarks>
    public partial class App : Application
    {
        /// <summary>How long the notification of a command-line run stays visible.</summary>
        private static readonly TimeSpan HeadlessNotificationTime = TimeSpan.FromSeconds(4);

        /// <summary>Maximum time to wait for the running instance to answer a forwarded <c>--apply</c>.</summary>
        private static readonly TimeSpan ForwardedApplyTimeout = TimeSpan.FromMinutes(2);

        private SingleInstance? _instance;

        /// <summary>User settings shared by the whole application.</summary>
        public static AppSettings Settings { get; } = AppSettings.Load();

        /// <inheritdoc />
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Loc.Apply(Settings.Language);
            var options = CommandLineOptions.Parse(e.Args);

            if (options.Error != null || options.ShowHelp)
            {
                Shutdown(ShowHelp(options));
                return;
            }

            _instance = SingleInstance.TryAcquire();
            if (_instance == null)
            {
                // Another instance is running: it does the work, this process only forwards the request
                Shutdown(await ForwardToRunningInstanceAsync(options));
                return;
            }

            if (options.ApplyProfileName != null)
            {
                Shutdown(await ApplyHeadlessAsync(options.ApplyProfileName));
                return;
            }

            ApplyTheme(Settings.SelectedTheme);

            var window = new MainWindow();
            MainWindow = window;
            window.Initialize();
            _instance.StartServer(command => HandleCommandAsync(window, command));

            if (options.Minimized)
                window.HideToTray();
            else if (options.Compact || Settings.CompactMode)
                window.EnterCompactMode();
            else
                window.Show();
        }

        /// <inheritdoc />
        protected override void OnExit(ExitEventArgs e)
        {
            _instance?.Dispose();
            base.OnExit(e);
        }

        /// <summary>Applies the UI theme.</summary>
        /// <param name="theme"><c>Light</c>, <c>Dark</c> or any other value to follow the system.</param>
        public static void ApplyTheme(string theme)
        {
            switch (theme)
            {
                case "Light":
                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    break;
                case "Dark":
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    break;
                default:
                    ApplicationThemeManager.ApplySystemTheme();
                    break;
            }
        }

        /// <summary>Shows the command-line help (and the parse error, if any).</summary>
        /// <returns>The process exit code.</returns>
        private static int ShowHelp(CommandLineOptions options)
        {
            if (options.Error != null)
            {
                MessageBox.Show(options.Error + "\n\n" + CommandLineOptions.HelpText, "IP Switch",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return CommandLineOptions.ExitError;
            }

            MessageBox.Show(CommandLineOptions.HelpText, "IP Switch", MessageBoxButton.OK, MessageBoxImage.Information);
            return CommandLineOptions.ExitSuccess;
        }

        /// <summary>
        /// Sends the request of this launch to the running instance: apply a profile, or just show its window.
        /// </summary>
        /// <returns>The process exit code.</returns>
        private static async Task<int> ForwardToRunningInstanceAsync(CommandLineOptions options)
        {
            if (options.ApplyProfileName == null)
            {
                await SingleInstance.SendAsync(SingleInstance.ShowCommand, TimeSpan.FromSeconds(5));
                return CommandLineOptions.ExitSuccess;
            }

            string? reply = await SingleInstance.SendAsync(
                SingleInstance.BuildApplyCommand(options.ApplyProfileName), ForwardedApplyTimeout);

            return int.TryParse(reply, out int exitCode) ? exitCode : CommandLineOptions.ExitError;
        }

        /// <summary>
        /// Handles a command received from a later launch (runs on a background thread; work is done on the UI thread).
        /// </summary>
        /// <param name="window">Main window.</param>
        /// <param name="command">Received command.</param>
        /// <returns>The reply for the other process.</returns>
        private Task<string> HandleCommandAsync(MainWindow window, string command)
        {
            return Dispatcher.Invoke(async () =>
            {
                if (SingleInstance.TryParseApplyCommand(command, out string name))
                {
                    var profile = ProfileService.FindByName(name);
                    if (profile == null)
                    {
                        window.ShowNotification(Loc.Get("Notify_Error"), Loc.Format("Cli_NotFound", name), Wpf.Ui.Controls.ControlAppearance.Danger);
                        return CommandLineOptions.ExitProfileNotFound.ToString();
                    }

                    var outcome = await window.ApplyProfileAsync(profile);
                    return (outcome is ApplyOutcome.Applied or ApplyOutcome.AppliedWithoutConnectivity
                        ? CommandLineOptions.ExitSuccess
                        : CommandLineOptions.ExitError).ToString();
                }

                window.ShowFromTray();
                return CommandLineOptions.ExitSuccess.ToString();
            });
        }

        /// <summary>
        /// Applies a profile without user interface (no instance was running), shows a brief notification and returns
        /// the exit code.
        /// </summary>
        /// <param name="profileName">Profile name.</param>
        /// <returns>The process exit code.</returns>
        private static async Task<int> ApplyHeadlessAsync(string profileName)
        {
            var profile = ProfileService.FindByName(profileName);
            if (profile == null)
            {
                await ShowHeadlessNotificationAsync(Loc.Format("Cli_NotFound", profileName), isError: true);
                return CommandLineOptions.ExitProfileNotFound;
            }

            // Without a user interface the user cannot be asked: connectivity is not verified
            var (outcome, message) = await ProfileApplier.ApplyAsync(profile, Settings, confirmKeep: null);
            bool success = outcome == ApplyOutcome.Applied;

            await ShowHeadlessNotificationAsync(
                success ? Loc.Format("Notify_Applied", profile.Name) : Loc.Format("Notify_ApplyFailed", profile.Name, message),
                isError: !success);

            return success ? CommandLineOptions.ExitSuccess : CommandLineOptions.ExitError;
        }

        /// <summary>Briefly shows a tray balloon (used in command-line mode).</summary>
        private static async Task ShowHeadlessNotificationAsync(string message, bool isError)
        {
            using var icon = new Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(AppPaths.ExecutablePath) ?? System.Drawing.SystemIcons.Application,
                Text = "IP Switch",
                Visible = true
            };
            icon.ShowBalloonTip((int)HeadlessNotificationTime.TotalMilliseconds, "IP Switch", message,
                isError ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Info);

            await Task.Delay(HeadlessNotificationTime);
            icon.Visible = false;
        }
    }
}
