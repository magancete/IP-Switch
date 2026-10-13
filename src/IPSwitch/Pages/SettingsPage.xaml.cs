using IPSwitch.Models;
using IPSwitch.Services;
using IPSwitch.Windows;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace IPSwitch.Pages
{
    /// <summary>
    /// Settings page. Every change is applied and saved immediately in <see cref="App.Settings"/>.
    /// </summary>
    /// <remarks>
    /// The page is cached by the navigation, so the controls are reloaded every time it is shown (some values can
    /// change elsewhere, e.g. the pin button in the title bar). Querying or changing the startup task runs
    /// <c>schtasks</c>, which is done on a background thread.
    /// </remarks>
    public partial class SettingsPage : Page
    {
        private readonly AppSettings _settings = App.Settings;
        private bool _isLoading;

        private static MainWindow? Main => Application.Current.MainWindow as MainWindow;

        /// <summary>Creates the page.</summary>
        public SettingsPage()
        {
            InitializeComponent();
            Loaded += async (_, _) => await LoadUiFromSettingsAsync();
        }

        private async Task LoadUiFromSettingsAsync()
        {
            _isLoading = true;
            try
            {
                CheckSubnet.IsChecked = _settings.ShowSubnetMask;
                CheckGateway.IsChecked = _settings.ShowGateway;
                CheckPrimaryDNS.IsChecked = _settings.ShowPrimaryDns;
                CheckSecondaryDNS.IsChecked = _settings.ShowSecondaryDns;
                CheckInterface.IsChecked = _settings.ShowInterface;

                ToggleConfirmExit.IsChecked = _settings.ConfirmExit;
                ToggleAlwaysOnTop.IsChecked = _settings.AlwaysOnTop;
                ToggleMinimizeToTray.IsChecked = _settings.MinimizeToTray;
                ToggleCompactFade.IsChecked = _settings.CompactFadeWhenInactive;
                ToggleVerify.IsChecked = _settings.VerifyConnectivity;
                ToggleUpdates.IsChecked = _settings.CheckForUpdates;
                VerifyHostBox.Text = _settings.VerifyHost;

                SelectByTag(ThemeComboBox, _settings.SelectedTheme);
                SelectByTag(LanguageComboBox, Loc.CurrentLanguage);
                SelectByTag(RevertTimeoutComboBox, _settings.RevertTimeoutSeconds.ToString());
                SelectByTag(TrayClickComboBox, _settings.TrayClickOpensFlyout ? "flyout" : "window");

                VersionText.Text = "v" + UpdateService.CurrentVersion;
                DataFolderText.Text = AppPaths.DisplayDataFolder;
                DataFolderText.ToolTip = AppPaths.DataFolder;
            }
            finally
            {
                _isLoading = false;
            }

            ToggleStartup.IsEnabled = false;
            ToggleStartup.IsChecked = await Task.Run(StartupService.IsEnabled);
            ToggleStartup.IsEnabled = true;
        }

        /// <summary>Selects the item whose <c>Tag</c> matches; if none does, the last one.</summary>
        private static void SelectByTag(ComboBox comboBox, string tag) =>
            comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag)
                                    ?? comboBox.Items[comboBox.Items.Count - 1];

        private static string? SelectedTag(ComboBox comboBox) =>
            (comboBox.SelectedItem as ComboBoxItem)?.Tag as string;

        /// <summary>Reads every control, applies the changes that take effect immediately and saves.</summary>
        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            if (_isLoading || !IsLoaded) return;

            _settings.ShowSubnetMask = CheckSubnet.IsChecked ?? true;
            _settings.ShowGateway = CheckGateway.IsChecked ?? true;
            _settings.ShowPrimaryDns = CheckPrimaryDNS.IsChecked ?? true;
            _settings.ShowSecondaryDns = CheckSecondaryDNS.IsChecked ?? true;
            _settings.ShowInterface = CheckInterface.IsChecked ?? true;

            _settings.ConfirmExit = ToggleConfirmExit.IsChecked ?? true;
            _settings.AlwaysOnTop = ToggleAlwaysOnTop.IsChecked ?? false;
            _settings.MinimizeToTray = ToggleMinimizeToTray.IsChecked ?? false;
            _settings.CompactFadeWhenInactive = ToggleCompactFade.IsChecked ?? true;
            _settings.VerifyConnectivity = ToggleVerify.IsChecked ?? true;
            _settings.CheckForUpdates = ToggleUpdates.IsChecked ?? true;
            _settings.VerifyHost = VerifyHostBox.Text?.Trim() ?? string.Empty;
            _settings.TrayClickOpensFlyout = SelectedTag(TrayClickComboBox) != "window";

            if (int.TryParse(SelectedTag(RevertTimeoutComboBox), out int timeout))
                _settings.RevertTimeoutSeconds = timeout;

            string theme = SelectedTag(ThemeComboBox) ?? "System";
            if (theme != _settings.SelectedTheme)
            {
                _settings.SelectedTheme = theme;
                App.ApplyTheme(theme);
            }

            string language = SelectedTag(LanguageComboBox) ?? Loc.CurrentLanguage;
            if (language != Loc.CurrentLanguage)
            {
                _settings.Language = language;
                Loc.Apply(language);
            }

            Main?.ApplyTopmost();
            _settings.Save();
        }

        private async void OnStartupToggled(object sender, RoutedEventArgs e)
        {
            bool requested = ToggleStartup.IsChecked == true;
            ToggleStartup.IsEnabled = false;
            try
            {
                if (await Task.Run(() => StartupService.SetEnabled(requested))) return;

                ToggleStartup.IsChecked = await Task.Run(StartupService.IsEnabled);
                Main?.ShowNotification(Loc.Get("Notify_Error"), Loc.Get("Set_StartupFailed"), ControlAppearance.Danger);
            }
            finally
            {
                ToggleStartup.IsEnabled = true;
            }
        }

        private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
        {
            if (Main == null) return;

            CheckUpdatesButton.IsEnabled = false;
            try
            {
                var update = await Main.CheckForUpdatesAsync(silent: false);
                if (update != null &&
                    await Main.ConfirmAsync(Loc.Get("Update_Title"), Loc.Format("Update_Available", update.Version), Loc.Get("Update_Open")))
                {
                    Process.Start(new ProcessStartInfo(update.Url) { UseShellExecute = true });
                }
            }
            finally
            {
                CheckUpdatesButton.IsEnabled = true;
            }
        }

        /// <summary>Opens the GitHub issue chooser in the browser; notifies if it cannot be opened.</summary>
        private void OnFeedbackClick(object sender, RoutedEventArgs e)
        {
            if (!IssueReporter.Open())
                Main?.ShowNotification(Loc.Get("Notify_Error"), Loc.Get("Set_FeedbackFailed"), ControlAppearance.Danger);
        }

        private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start("explorer.exe", $"\"{AppPaths.DataFolder}\"");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsPage] {ex.Message}");
            }
        }
    }
}
