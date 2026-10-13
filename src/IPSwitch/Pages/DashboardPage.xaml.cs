using IPSwitch.Models;
using IPSwitch.Services;
using IPSwitch.ViewModels;
using IPSwitch.Windows;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace IPSwitch.Pages
{
    /// <summary>
    /// Home page: state of every adapter and quick actions
    /// (save as profile, renew DHCP, restart, enable/disable, hide, WiFi networks and flush DNS cache).
    /// </summary>
    /// <remarks>
    /// The page is cached by the navigation (created once). Each time it is shown, and whenever the network or the
    /// language changes, it refreshes asynchronously: the previous data stays visible until the new data arrives.
    /// </remarks>
    public partial class DashboardPage : Page
    {
        private readonly DashboardPageViewModel _viewModel = new();
        private readonly DispatcherTimer _refreshDebounce;

        private static MainWindow? Main => Application.Current.MainWindow as MainWindow;

        /// <summary>Creates the page.</summary>
        public DashboardPage()
        {
            InitializeComponent();
            DataContext = _viewModel;

            // Network changes arrive in bursts: they are coalesced before refreshing
            _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _refreshDebounce.Tick += (_, _) =>
            {
                _refreshDebounce.Stop();
                Refresh();
            };

            Loaded += (_, _) =>
            {
                NetworkChange.NetworkAddressChanged += OnNetworkChanged;
                Loc.LanguageChanged += OnNetworkChanged;
                Refresh();
            };
            Unloaded += (_, _) =>
            {
                NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
                Loc.LanguageChanged -= OnNetworkChanged;
            };
        }

        private void Refresh() => _ = _viewModel.RefreshAsync();

        // May arrive on a system thread: rescheduled on the Dispatcher
        private void OnNetworkChanged(object? sender, EventArgs e) =>
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _refreshDebounce.Stop();
                _refreshDebounce.Start();
            }));

        private static NetworkInterfaceDetail? GetDetail(object sender) =>
            (sender as FrameworkElement)?.DataContext as NetworkInterfaceDetail;

        /// <summary>Opens the profile editor pre-filled with the adapter's current configuration.</summary>
        private async void OnSaveAsProfileClick(object sender, RoutedEventArgs e)
        {
            if (GetDetail(sender) is not { } detail) return;

            var snapshot = await Task.Run(() => NetworkService.CaptureCurrentConfig(detail.Name));
            if (snapshot == null)
            {
                Main?.ShowNotification(Loc.Get("Notify_Error"), Loc.Get("Dash_CaptureFailed"), ControlAppearance.Danger);
                return;
            }

            snapshot.Name = ProfileTransferService.GetUniqueName(ProfileService.Profiles,
                Loc.Format("Dash_CurrentProfileName", detail.Name));

            var window = new AddEditWindow(ProfileService.Profiles.ToList(), snapshot, isNew: true)
            {
                Owner = Window.GetWindow(this)
            };

            if (window.ShowDialog() != true || window.ResultProfile == null) return;

            ProfileService.Profiles.Add(window.ResultProfile);
            ProfileService.Save();
            await ProfileService.DetectActiveProfilesAsync();
            Main?.ShowNotification(Loc.Get("Notify_Success"), Loc.Format("Dash_ProfileSaved", window.ResultProfile.Name), ControlAppearance.Success);
        }

        private async void OnRenewDhcpClick(object sender, RoutedEventArgs e)
        {
            if (GetDetail(sender) is not { } detail) return;
            await RunAdapterActionAsync(() => NetworkService.RenewDhcpAsync(detail.Name),
                Loc.Format("Dash_RenewDone", detail.Name), sender);
        }

        private async void OnRestartAdapterClick(object sender, RoutedEventArgs e)
        {
            if (GetDetail(sender) is not { } detail || Main == null) return;

            bool confirmed = await Main.ConfirmAsync(Loc.Get("Dash_Restart"),
                Loc.Format("Dash_RestartConfirm", detail.Name), Loc.Get("Dash_Restart"));
            if (!confirmed) return;

            await RunAdapterActionAsync(() => NetworkService.RestartAdapterAsync(detail.Name),
                Loc.Format("Dash_RestartDone", detail.Name), sender);
        }

        private async void OnDisableAdapterClick(object sender, RoutedEventArgs e)
        {
            if (GetDetail(sender) is not { } detail || Main == null) return;

            bool confirmed = await Main.ConfirmAsync(Loc.Get("Dash_Disable"),
                Loc.Format("Dash_DisableConfirm", detail.Name), Loc.Get("Dash_Disable"));
            if (!confirmed) return;

            await RunAdapterActionAsync(() => AdapterService.SetEnabledAsync(detail.Name, enabled: false),
                Loc.Format("Dash_DisableDone", detail.Name), sender);
        }

        private async void OnEnableAdapterClick(object sender, RoutedEventArgs e)
        {
            if (GetDetail(sender) is not { } detail) return;
            await RunAdapterActionAsync(() => AdapterService.SetEnabledAsync(detail.Name, enabled: true),
                Loc.Format("Dash_EnableDone", detail.Name), sender);
        }

        private void OnWifiNetworksClick(object sender, RoutedEventArgs e)
        {
            if (!AdapterService.OpenWifiNetworks())
                Main?.ShowNotification(Loc.Get("Notify_Error"), Loc.Get("Dash_WifiNetworksFailed"), ControlAppearance.Danger);
        }

        private void OnHideAdapterClick(object sender, RoutedEventArgs e) => SetHidden(sender, hidden: true);

        private void OnUnhideAdapterClick(object sender, RoutedEventArgs e) => SetHidden(sender, hidden: false);

        /// <summary>Hides or shows an adapter, saves the preference and notifies the other views.</summary>
        private void SetHidden(object sender, bool hidden)
        {
            if (GetDetail(sender) is not { } detail) return;

            AdapterService.SetHidden(App.Settings.HiddenAdapters, detail.Name, hidden);
            App.Settings.Save();
            AdapterService.NotifyHiddenAdaptersChanged();
            Refresh();
        }

        private async void OnFlushDnsClick(object sender, RoutedEventArgs e)
        {
            if (Main != null) await Main.FlushDnsAsync();
        }

        /// <summary>Runs an adapter action, disabling its button meanwhile, and notifies the result.</summary>
        private async Task RunAdapterActionAsync(Func<Task<CommandResult>> action, string successMessage, object sender)
        {
            var button = sender as UIElement;
            if (button != null) button.IsEnabled = false;

            try
            {
                var result = await action();
                Main?.ShowNotification(
                    Loc.Get(result.Success ? "Notify_Success" : "Notify_Error"),
                    result.Success ? successMessage : result.Message,
                    result.Success ? ControlAppearance.Success : ControlAppearance.Danger);
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
                Refresh();
            }
        }
    }
}
