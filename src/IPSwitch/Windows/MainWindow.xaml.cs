using IPSwitch.Models;
using IPSwitch.Pages;
using IPSwitch.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using Forms = System.Windows.Forms;

namespace IPSwitch.Windows
{
    /// <summary>
    /// Main window and coordinator of the running application.
    /// </summary>
    /// <remarks>
    /// Besides navigation (Home, Profiles, Settings), it centralizes:
    /// <list type="bullet">
    /// <item><description>The tray icon (menu, flyout and tooltip with the network state).</description></item>
    /// <item><description>Switching between the full view and the compact view (<see cref="CompactWindow"/>).</description></item>
    /// <item><description>Applying profiles with notifications and rollback (<see cref="ApplyProfileAsync"/>).</description></item>
    /// <item><description>Global hotkeys (<see cref="HotkeyService"/>) and WiFi auto-switch (<see cref="AutoSwitchService"/>).</description></item>
    /// <item><description>Closing the application (to the tray or with confirmation).</description></item>
    /// </list>
    /// </remarks>
    public partial class MainWindow : FluentWindow
    {
        /// <summary>Maximum length of the tray icon text on .NET Framework.</summary>
        private const int TrayTextMaxLength = 63;

        private readonly AppSettings _settings = App.Settings;
        private readonly DispatcherTimer _trayTextDebounce;
        private Forms.NotifyIcon? _trayIcon;
        private CompactWindow? _compactWindow;
        private TrayFlyoutWindow? _trayFlyout;
        private HotkeyService? _hotkeys;
        private AutoSwitchService? _autoSwitch;
        private string _lastHotkeyFailures = string.Empty;
        private bool _isExiting;
        private bool _trayHintShown;

        /// <summary>Creates the main window. Call <see cref="Initialize"/> before showing any view.</summary>
        public MainWindow()
        {
            InitializeComponent();
            Topmost = _settings.AlwaysOnTop;
            PinButton.IsChecked = _settings.AlwaysOnTop;

            _trayTextDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _trayTextDebounce.Tick += (_, _) =>
            {
                _trayTextDebounce.Stop();
                _ = UpdateTrayTextAsync();
            };

            Loaded += (_, _) => NavigationView.Navigate(typeof(DashboardPage));
        }

        /// <summary>
        /// Initializes the background services (tray, hotkeys, WiFi auto-switch, update check).
        /// </summary>
        /// <remarks>Must be called once, even if the window is never shown (start minimized or in compact view).</remarks>
        public void Initialize()
        {
            CreateTrayIcon();

            _hotkeys = new HotkeyService(this, profile => _ = ApplyProfileAsync(profile));
            RegisterHotkeys();

            _autoSwitch = new AutoSwitchService(ApplyProfileAsync);
            _autoSwitch.Start();

            ProfileService.Saved += (_, _) =>
            {
                RegisterHotkeys();
                ScheduleTrayTextUpdate();
            };
            AdapterService.HiddenAdaptersChanged += (_, _) => ScheduleTrayTextUpdate();
            NetworkChange.NetworkAddressChanged += (_, _) => Dispatcher.BeginInvoke(new Action(ScheduleTrayTextUpdate));

            if (_settings.CheckForUpdates)
                _ = CheckForUpdatesAsync(silent: true);
        }

        #region Notifications

        /// <summary>
        /// Shows a notification in the visible window (main or compact) or, if none is visible,
        /// as a tray balloon.
        /// </summary>
        /// <param name="title">Title.</param>
        /// <param name="message">Message.</param>
        /// <param name="appearance">Appearance (information, success, error…).</param>
        public void ShowNotification(string title, string message, ControlAppearance appearance)
        {
            SnackbarPresenter? presenter = IsVisible ? SnackbarPresenter
                : _compactWindow?.IsVisible == true ? _compactWindow.Notifications
                : null;

            if (presenter == null)
            {
                var icon = appearance == ControlAppearance.Danger ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Info;
                _trayIcon?.ShowBalloonTip(4000, title, string.IsNullOrEmpty(message) ? title : message, icon);
                return;
            }

            new Snackbar(presenter)
            {
                Title = title,
                Content = message,
                Appearance = appearance,
                Timeout = TimeSpan.FromSeconds(appearance == ControlAppearance.Danger ? 8 : 4)
            }.Show();
        }

        /// <summary>Shows a confirmation dialog in the main window.</summary>
        /// <param name="title">Title.</param>
        /// <param name="message">Message.</param>
        /// <param name="primaryButton">Text of the confirmation button.</param>
        /// <returns><see langword="true"/> if the user confirmed.</returns>
        public Task<bool> ConfirmAsync(string title, string message, string primaryButton) =>
            DialogService.ConfirmAsync(RootContentDialog, title, message, primaryButton);

        #endregion

        #region Applying profiles

        /// <summary>
        /// Applies a profile with the complete flow: validation, notifications, connectivity check
        /// and optional rollback (see <see cref="ProfileApplier"/>).
        /// </summary>
        /// <param name="profile">Profile to apply.</param>
        /// <returns>The outcome, once the profile has been applied or rolled back.</returns>
        public async Task<ApplyOutcome> ApplyProfileAsync(IPProfile profile)
        {
            if (ProfileApplier.IsBusy)
            {
                ShowNotification(Loc.Get("Notify_Busy"), string.Empty, ControlAppearance.Caution);
                return ApplyOutcome.Busy;
            }

            ShowNotification(Loc.Get("Notify_Applying"),
                Loc.Format("Notify_ApplyingMsg", profile.Name, profile.Interface), ControlAppearance.Info);

            var (outcome, message) = await ProfileApplier.ApplyAsync(
                profile, _settings, seconds => RevertPromptWindow.AskAsync(profile.Name, seconds));

            switch (outcome)
            {
                case ApplyOutcome.Applied:
                case ApplyOutcome.AppliedWithoutConnectivity:
                    ShowNotification(Loc.Get("Notify_Success"), Loc.Format("Notify_Applied", profile.Name), ControlAppearance.Success);
                    break;
                case ApplyOutcome.RolledBack:
                    ShowNotification(Loc.Get("Notify_RolledBack"), Loc.Format("Notify_RolledBackMsg", profile.Name), ControlAppearance.Caution);
                    break;
                case ApplyOutcome.Invalid:
                case ApplyOutcome.Failed:
                    ShowNotification(Loc.Get("Notify_Error"), Loc.Format("Notify_ApplyFailed", profile.Name, message), ControlAppearance.Danger);
                    break;
            }

            await UpdateTrayTextAsync();
            return outcome;
        }

        #endregion

        #region Hotkeys

        private void RegisterHotkeys()
        {
            if (_hotkeys == null) return;

            var failed = _hotkeys.RegisterAll(ProfileService.Profiles.Where(p => p.IsValid));
            string failedNames = string.Join(", ", failed.Select(p => $"{p.Name} ({p.Hotkey})"));

            // Only notify when the set of failed hotkeys changes, not on every save
            if (failedNames.Length > 0 && failedNames != _lastHotkeyFailures)
                ShowNotification(Loc.Get("Hotkey_FailedTitle"), Loc.Format("Hotkey_Failed", failedNames), ControlAppearance.Caution);

            _lastHotkeyFailures = failedNames;
        }

        #endregion

        #region Compact view

        /// <summary>Switches to the compact view and remembers the preference.</summary>
        public void EnterCompactMode()
        {
            _settings.CompactMode = true;
            _settings.Save();

            _compactWindow ??= new CompactWindow(this);
            Hide();
            _compactWindow.Show();
            _compactWindow.Activate();
        }

        /// <summary>Returns to the full view and remembers the preference.</summary>
        public void ExitCompactMode()
        {
            _settings.CompactMode = false;
            _settings.Save();

            _compactWindow?.Hide();
            ShowWindow(this);
        }

        /// <summary>Applies the "always on top" setting to every window and syncs the pin buttons.</summary>
        public void ApplyTopmost()
        {
            Topmost = _settings.AlwaysOnTop;
            PinButton.IsChecked = _settings.AlwaysOnTop;
            _compactWindow?.ApplyTopmost();
        }

        private void OnCompactClick(object sender, RoutedEventArgs e) => EnterCompactMode();

        /// <summary>Turns "always on top" on or off from the title bar and saves it.</summary>
        private void OnPinClick(object sender, RoutedEventArgs e)
        {
            _settings.AlwaysOnTop = PinButton.IsChecked == true;
            _settings.Save();
            ApplyTopmost();
        }

        #endregion

        #region Tray

        private void CreateTrayIcon()
        {
            var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/favicon.ico"))?.Stream;

            _trayIcon = new Forms.NotifyIcon
            {
                Icon = iconStream != null ? new System.Drawing.Icon(iconStream) : System.Drawing.SystemIcons.Application,
                Text = "IP Switch",
                Visible = true,
                ContextMenuStrip = new Forms.ContextMenuStrip()
            };

            _trayIcon.MouseClick += (_, e) =>
            {
                if (e.Button != Forms.MouseButtons.Left) return;
                if (_settings.TrayClickOpensFlyout)
                    (_trayFlyout ??= new TrayFlyoutWindow(this)).Toggle();
                else
                    ShowFromTray();
            };
            _trayIcon.MouseDoubleClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left) ShowFromTray();
            };
            _trayIcon.ContextMenuStrip.Opening += (_, _) => BuildTrayMenu();

            ScheduleTrayTextUpdate();
        }

        // The menu is rebuilt when opened so it reflects the current language and profiles
        private void BuildTrayMenu()
        {
            var menu = _trayIcon!.ContextMenuStrip!;
            menu.Items.Clear();

            menu.Items.Add(Loc.Get("Tray_Show"), null, (_, _) => ShowFromTray());
            menu.Items.Add(Loc.Get(_settings.CompactMode ? "Compact_Expand" : "Compact_Enter"), null, (_, _) =>
            {
                if (_settings.CompactMode) ExitCompactMode();
                else EnterCompactMode();
            });

            var applyMenu = new Forms.ToolStripMenuItem(Loc.Get("Tray_ApplyProfile"));
            foreach (var profile in ProfileService.Profiles.OrderBy(p => p.Interface).ThenBy(p => p.Name))
            {
                var item = new Forms.ToolStripMenuItem($"{profile.Name}  ({profile.Interface})")
                {
                    Checked = profile.IsActive,
                    Enabled = profile.IsValid
                };
                item.Click += async (_, _) => await ApplyProfileAsync(profile);
                applyMenu.DropDownItems.Add(item);
            }
            applyMenu.Enabled = applyMenu.DropDownItems.Count > 0;
            menu.Items.Add(applyMenu);

            menu.Items.Add(Loc.Get("Dash_FlushDns"), null, async (_, _) => await FlushDnsAsync());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(Loc.Get("Tray_Exit"), null, (_, _) => ExitApplication());
        }

        /// <summary>Flushes the DNS cache and notifies the result.</summary>
        /// <returns>The operation task.</returns>
        public async Task FlushDnsAsync()
        {
            var result = await NetworkService.FlushDnsAsync();
            ShowNotification(
                Loc.Get(result.Success ? "Notify_Success" : "Notify_Error"),
                result.Success ? Loc.Get("Dash_FlushDnsDone") : result.Message,
                result.Success ? ControlAppearance.Success : ControlAppearance.Danger);
        }

        private void ScheduleTrayTextUpdate()
        {
            _trayTextDebounce.Stop();
            _trayTextDebounce.Start();
        }

        /// <summary>
        /// Updates the tray tooltip with the active profile, or the IP address, of each connected adapter.
        /// </summary>
        /// <returns>A task that completes when the text has been updated.</returns>
        /// <remarks>Active profiles are read on the UI thread; adapters are queried on a background thread.</remarks>
        private async Task UpdateTrayTextAsync()
        {
            if (_trayIcon == null) return;

            var activeByInterface = ProfileService.Profiles
                .Where(p => p.IsActive)
                .GroupBy(p => p.Interface, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
            var hidden = _settings.HiddenAdapters.ToList();

            try
            {
                var lines = await Task.Run(() => AdapterService.GetVisibleInterfaces(hidden)
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .OrderBy(n => n.Name)
                    .Select(n => $"{n.Name}: {(activeByInterface.TryGetValue(n.Name, out var name) ? name : NetworkService.GetIPv4Address(n) ?? "-")}")
                    .ToList());

                if (_trayIcon != null)
                    _trayIcon.Text = BuildTrayText(lines);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Tray text update failed: {ex.Message}");
            }
        }

        /// <summary>Builds the tray icon text within the 63-character limit.</summary>
        /// <param name="lines">One line per adapter.</param>
        /// <returns>The text ready for <see cref="Forms.NotifyIcon.Text"/>.</returns>
        public static string BuildTrayText(IEnumerable<string> lines)
        {
            string text = string.Join("\n", new[] { "IP Switch" }.Concat(lines));
            return text.Length <= TrayTextMaxLength ? text : text.Substring(0, TrayTextMaxLength - 1) + "…";
        }

        /// <summary>Hides every window; the application keeps running in the notification area.</summary>
        public void HideToTray()
        {
            Hide();
            _compactWindow?.Hide();

            // Nothing is visible: give the WPF working set back to Windows once the windows are gone
            Dispatcher.BeginInvoke(new Action(PerformanceTuning.TrimWorkingSet), DispatcherPriority.ApplicationIdle);

            if (_trayHintShown) return;
            _trayHintShown = true;
            _trayIcon?.ShowBalloonTip(3000, "IP Switch", Loc.Get("Tray_StillRunning"), Forms.ToolTipIcon.Info);
        }

        /// <summary>Shows the current view (full or compact) and brings it to the front.</summary>
        public void ShowFromTray()
        {
            if (_settings.CompactMode)
            {
                _compactWindow ??= new CompactWindow(this);
                ShowWindow(_compactWindow);
            }
            else
            {
                ShowWindow(this);
            }
        }

        private static void ShowWindow(Window window)
        {
            window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Activate();
        }

        #endregion

        #region Updates

        /// <summary>Checks GitHub for a newer version.</summary>
        /// <param name="silent">If <see langword="true"/>, only notifies when there is a new version.</param>
        /// <returns>The new version information, or <see langword="null"/>.</returns>
        public async Task<UpdateInfo?> CheckForUpdatesAsync(bool silent)
        {
            var update = await UpdateService.CheckAsync();

            if (update != null)
                ShowNotification(Loc.Get("Update_Title"), Loc.Format("Update_Available", update.Version), ControlAppearance.Info);
            else if (!silent)
                ShowNotification(Loc.Get("Update_Title"), Loc.Get("Update_UpToDate"), ControlAppearance.Success);

            return update;
        }

        #endregion

        #region Closing

        /// <summary>Whether the application is shutting down (window closes must no longer be intercepted).</summary>
        public bool IsExiting => _isExiting;

        private async void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            if (_isExiting) return;
            e.Cancel = true;
            await RequestCloseAsync(RootContentDialog);
        }

        /// <summary>
        /// Handles a close request from any window: hides to the tray or exits (with confirmation if enabled).
        /// </summary>
        /// <param name="dialogHost">Dialog host of the window requesting the close.</param>
        /// <returns>The operation task.</returns>
        public async Task RequestCloseAsync(ContentDialogHost dialogHost)
        {
            if (_settings.MinimizeToTray)
            {
                HideToTray();
                return;
            }

            if (_settings.ConfirmExit &&
                !await DialogService.ConfirmAsync(dialogHost, Loc.Get("Exit_Title"), Loc.Get("Exit_Message"), Loc.Get("Exit_Confirm")))
                return;

            ExitApplication();
        }

        /// <summary>Releases resources (tray, hotkeys, services) and closes the application.</summary>
        public void ExitApplication()
        {
            _isExiting = true;
            _settings.Save();

            _autoSwitch?.Dispose();
            _hotkeys?.Dispose();

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            _trayFlyout?.Close();
            Application.Current.Shutdown();
        }

        #endregion
    }
}
