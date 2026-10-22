using IPSwitch.Models;
using IPSwitch.Pages;
using IPSwitch.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
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
        private System.Windows.Controls.ContextMenu? _trayMenu;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);
        private HotkeyService? _hotkeys;
        private AutoSwitchService? _autoSwitch;
        private string _lastHotkeyFailures = string.Empty;
        private bool _isExiting;
        private bool _trayHintShown;

        /// <summary>Creates the main window. Call <see cref="Initialize"/> before showing any view.</summary>
        public MainWindow()
        {
            InitializeComponent();
            ApplyInitialBounds();
            SizeChanged += (_, _) => TrackBounds();
            LocationChanged += (_, _) => TrackBounds();
            StateChanged += (_, _) => TrackBounds();
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
        /// Sizes the window for the current screen: the last bounds if they are still visible, otherwise the size
        /// from the XAML capped to the work area and centered (<see cref="WindowPlacement.Fit"/>).
        /// </summary>
        private void ApplyInitialBounds()
        {
            Rect? saved = _settings.MainLeft is double left && _settings.MainTop is double top &&
                          _settings.MainWidth is double width && _settings.MainHeight is double height
                ? new Rect(left, top, width, height)
                : null;

            var virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var bounds = WindowPlacement.Fit(saved, SystemParameters.WorkArea, virtualScreen,
                new Size(Width, Height), new Size(MinWidth, MinHeight));

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            if (_settings.MainMaximized) WindowState = WindowState.Maximized;
        }

        /// <summary>Remembers the normal bounds and the maximized state (saved with the other settings).</summary>
        private void TrackBounds()
        {
            if (!IsLoaded || WindowState == WindowState.Minimized) return;

            _settings.MainMaximized = WindowState == WindowState.Maximized;
            if (WindowState != WindowState.Normal) return;

            _settings.MainLeft = Left;
            _settings.MainTop = Top;
            _settings.MainWidth = ActualWidth;
            _settings.MainHeight = ActualHeight;
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

            _ = RepairAfterMoveAsync();
            AnnounceUpdateIfNeeded();
        }

        /// <summary>Tells the user once that the app runs a new version (after an in-app update or a manual one).</summary>
        private void AnnounceUpdateIfNeeded()
        {
            string current = UpdateService.CurrentVersion.ToString();
            if (_settings.LastRunVersion == current) return;

            bool updated = !string.IsNullOrEmpty(_settings.LastRunVersion);
            _settings.LastRunVersion = current;
            _settings.Save();
            if (updated)
                ShowNotification(Loc.Get("Update_Title"), Loc.Format("Update_Done", current), ControlAppearance.Success);
        }

        /// <summary>
        /// Keeps what points to the executable working after it was moved or updated, in the background: the startup
        /// task (<see cref="SynchronizeStartupTaskAsync"/>) and the desktop shortcuts of the profiles.
        /// </summary>
        private async Task RepairAfterMoveAsync()
        {
            // They belong to the regular installation: copies run with an isolated data folder (tests, development)
            // must not adopt them or point them to themselves
            if (AppPaths.IsDataFolderOverridden) return;

            await SynchronizeStartupTaskAsync();

            int repaired = await Task.Run(ShortcutService.RepairDesktopShortcuts);
            if (repaired > 0)
                ShowNotification(Loc.Get("Shortcut_Title"), Loc.Format("Shortcut_Repaired", repaired), ControlAppearance.Info);
        }

        /// <summary>
        /// Repairs the startup task (missing, or registered for another copy of the executable) and adopts a task
        /// created by an earlier version.
        /// </summary>
        private async Task SynchronizeStartupTaskAsync()
        {
            bool wanted = _settings.StartWithWindows;
            bool actual = await Task.Run(() => StartupService.Synchronize(wanted));
            if (actual == wanted) return;

            _settings.StartWithWindows = actual;
            _settings.Save();
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
                Visible = true
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
            _trayIcon.MouseUp += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Right) ShowTrayMenu();
            };

            ScheduleTrayTextUpdate();
        }

        /// <summary>
        /// Shows the tray context menu as a WPF menu, so it gets the Windows 11 (Fluent) look and the app theme
        /// instead of the classic WinForms menu. It is rebuilt every time to reflect the language and profiles.
        /// </summary>
        private void ShowTrayMenu()
        {
            _trayMenu ??= CreateTrayMenu();
            BuildTrayMenu(_trayMenu);
            _trayMenu.IsOpen = true;
        }

        private System.Windows.Controls.ContextMenu CreateTrayMenu()
        {
            var menu = new System.Windows.Controls.ContextMenu
            {
                Placement = PlacementMode.MousePoint,
                StaysOpen = false
            };

            // A tray menu must belong to the foreground window, or it would not close when clicking elsewhere
            menu.Opened += (_, _) =>
            {
                if (PresentationSource.FromVisual(menu) is HwndSource source)
                    SetForegroundWindow(source.Handle);
            };
            return menu;
        }

        private void BuildTrayMenu(System.Windows.Controls.ContextMenu menu)
        {
            menu.Items.Clear();

            menu.Items.Add(TrayItem(Loc.Get("Tray_Show"), SymbolRegular.Open24, ShowFromTray));
            menu.Items.Add(TrayItem(
                Loc.Get(_settings.CompactMode ? "Compact_Expand" : "Compact_Enter"),
                _settings.CompactMode ? SymbolRegular.ArrowMaximize24 : SymbolRegular.ArrowMinimize24,
                () =>
                {
                    if (_settings.CompactMode) ExitCompactMode();
                    else EnterCompactMode();
                }));

            var applyMenu = TrayItem(Loc.Get("Tray_ApplyProfile"), SymbolRegular.Play24, null);
            foreach (var profile in ProfileService.Profiles.OrderBy(p => p.Interface).ThenBy(p => p.Name))
            {
                var item = TrayItem($"{profile.Name}  ({profile.Interface})", null, () => _ = ApplyProfileAsync(profile));
                item.IsChecked = profile.IsActive;
                item.IsEnabled = profile.IsValid;
                applyMenu.Items.Add(item);
            }
            applyMenu.IsEnabled = applyMenu.Items.Count > 0;
            menu.Items.Add(applyMenu);

            menu.Items.Add(TrayItem(Loc.Get("Dash_FlushDns"), SymbolRegular.Broom24, () => _ = FlushDnsAsync()));
            menu.Items.Add(new System.Windows.Controls.Separator());
            menu.Items.Add(TrayItem(Loc.Get("Tray_Exit"), SymbolRegular.Power24, ExitApplication));
        }

        private static System.Windows.Controls.MenuItem TrayItem(string header, SymbolRegular? symbol, Action? onClick)
        {
            var item = new System.Windows.Controls.MenuItem { Header = header };
            if (symbol is SymbolRegular icon)
                item.Icon = new SymbolIcon { Symbol = icon };
            if (onClick != null)
                item.Click += (_, _) => onClick();
            return item;
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

            // A manual check answers with a dialog (Settings), so only the startup check needs a notification
            if (update != null && silent)
                ShowNotification(Loc.Get("Update_Title"),
                    Loc.Format(update.CanInstall ? "Update_AvailableInstall" : "Update_Available", update.Version),
                    ControlAppearance.Info);
            else if (update == null && !silent)
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

        /// <summary>
        /// Installs <paramref name="update"/> (download, checksum, swap) and restarts into the new version.
        /// On failure the current version keeps running and the reason is shown.
        /// </summary>
        /// <param name="update">Release to install.</param>
        /// <returns>A task that completes when the update failed (on success the app exits).</returns>
        public async Task InstallUpdateAsync(UpdateInfo update)
        {
            ShowNotification(Loc.Get("Update_Title"), Loc.Format("Update_Downloading", update.Version), ControlAppearance.Info);

            var result = await SelfUpdater.InstallAsync(update);
            if (!result.Success)
            {
                ShowNotification(Loc.Get("Update_Title"), Loc.Format("Update_Failed", result.Message), ControlAppearance.Danger);
                return;
            }

            // The new process must be able to become the single instance before this one finishes closing
            ((App)Application.Current).ReleaseInstance();
            Process.Start(new ProcessStartInfo(AppPaths.ExecutablePath) { UseShellExecute = false });
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
