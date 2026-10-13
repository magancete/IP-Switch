using IPSwitch.Services;

namespace IPSwitch.Models
{
    /// <summary>
    /// User preferences, persisted in <c>settings.json</c> inside <see cref="AppPaths.DataFolder"/>.
    /// </summary>
    /// <remarks>
    /// A single shared instance lives in <see cref="App.Settings"/>. Every property has a default value so files
    /// written by previous versions stay compatible.
    /// </remarks>
    public class AppSettings
    {
        private static readonly string SettingsPath = AppPaths.GetDataFile("settings.json");

        #region Visible columns on the Profiles page

        /// <summary>Shows the "Mask" column.</summary>
        public bool ShowSubnetMask { get; set; } = true;

        /// <summary>Shows the "Gateway" column.</summary>
        public bool ShowGateway { get; set; } = true;

        /// <summary>Shows the "Primary DNS" column.</summary>
        public bool ShowPrimaryDns { get; set; } = true;

        /// <summary>Shows the "Secondary DNS" column.</summary>
        public bool ShowSecondaryDns { get; set; } = true;

        /// <summary>Shows the "Interface" column.</summary>
        public bool ShowInterface { get; set; } = true;

        #endregion

        #region Adapters

        /// <summary>
        /// Names of the adapters hidden by the user. They are not shown on Home, the compact view, the tray flyout
        /// or the profile editor (see <see cref="AdapterService"/>).
        /// </summary>
        public List<string> HiddenAdapters { get; set; } = new();

        #endregion

        #region Behavior

        /// <summary>Asks for confirmation before exiting the application.</summary>
        public bool ConfirmExit { get; set; } = true;

        /// <summary>Keeps the windows above every other window.</summary>
        public bool AlwaysOnTop { get; set; } = false;

        /// <summary>When the window is closed, the application keeps running in the notification area.</summary>
        public bool MinimizeToTray { get; set; } = false;

        /// <summary>
        /// Left-click action on the tray icon: <see langword="true"/> opens the quick panel,
        /// <see langword="false"/> opens the window.
        /// </summary>
        public bool TrayClickOpensFlyout { get; set; } = true;

        #endregion

        #region Compact view

        /// <summary>Last view mode used; restored at startup.</summary>
        public bool CompactMode { get; set; } = false;

        /// <summary>Fades the compact view when it loses focus.</summary>
        public bool CompactFadeWhenInactive { get; set; } = true;

        /// <summary>Saved horizontal position of the compact view, or <see langword="null"/> if none.</summary>
        public double? CompactLeft { get; set; }

        /// <summary>Saved vertical position of the compact view, or <see langword="null"/> if none.</summary>
        public double? CompactTop { get; set; }

        /// <summary>Saved width of the compact view, or <see langword="null"/> for the default.</summary>
        public double? CompactWidth { get; set; }

        /// <summary>Saved height of the compact view, or <see langword="null"/> to fit its content.</summary>
        public double? CompactHeight { get; set; }

        #endregion

        #region Connectivity check

        /// <summary>After applying a profile, checks connectivity and offers to roll back if it fails.</summary>
        public bool VerifyConnectivity { get; set; } = true;

        /// <summary>Host to check (IP or name). Empty = use the profile's gateway.</summary>
        public string VerifyHost { get; set; } = string.Empty;

        /// <summary>Countdown seconds before rolling back automatically.</summary>
        public int RevertTimeoutSeconds { get; set; } = 15;

        #endregion

        #region Appearance and updates

        /// <summary>Theme: <c>Light</c>, <c>Dark</c> or <c>System</c>.</summary>
        public string SelectedTheme { get; set; } = "System";

        /// <summary>Language: <c>en</c>, <c>es</c> or empty to use the system language.</summary>
        public string Language { get; set; } = string.Empty;

        /// <summary>Checks GitHub for a newer version at startup.</summary>
        public bool CheckForUpdates { get; set; } = true;

        #endregion

        /// <summary>Saves the settings to disk. Write errors are logged and ignored.</summary>
        public void Save() => JsonStore.Save(SettingsPath, this);

        /// <summary>Loads the settings from disk, or returns the defaults if they do not exist or are corrupt.</summary>
        /// <returns>The loaded settings.</returns>
        public static AppSettings Load()
        {
            var settings = JsonStore.Load<AppSettings>(SettingsPath) ?? new AppSettings();
            settings.HiddenAdapters ??= new List<string>(); // Hand-edited files may contain null
            return settings;
        }
    }
}
