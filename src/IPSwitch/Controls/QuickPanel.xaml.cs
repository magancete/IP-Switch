using IPSwitch.Services;
using IPSwitch.ViewModels;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace IPSwitch.Controls
{
    /// <summary>
    /// Reusable quick panel: adapters, their IP address and buttons to apply profiles with one click.
    /// </summary>
    /// <remarks>
    /// While loaded it refreshes itself on network, profile, hidden-adapter or language changes (with a short delay
    /// to coalesce bursts of events). The host must set a <see cref="QuickPanelViewModel"/> as <c>DataContext</c>.
    /// </remarks>
    public partial class QuickPanel : UserControl
    {
        private readonly DispatcherTimer _refreshDebounce;

        /// <summary>Creates the panel.</summary>
        public QuickPanel()
        {
            InitializeComponent();

            _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _refreshDebounce.Tick += (_, _) =>
            {
                _refreshDebounce.Stop();
                Refresh();
            };

            Loaded += (_, _) =>
            {
                NetworkChange.NetworkAddressChanged += OnChanged;
                ProfileService.Saved += OnChanged;
                AdapterService.HiddenAdaptersChanged += OnChanged;
                Loc.LanguageChanged += OnChanged;
                Refresh();
            };
            Unloaded += (_, _) =>
            {
                NetworkChange.NetworkAddressChanged -= OnChanged;
                ProfileService.Saved -= OnChanged;
                AdapterService.HiddenAdaptersChanged -= OnChanged;
                Loc.LanguageChanged -= OnChanged;
            };
        }

        private void Refresh() => _ = (DataContext as QuickPanelViewModel)?.RefreshAsync();

        private void OnWifiNetworksClick(object sender, RoutedEventArgs e) => AdapterService.OpenWifiNetworks();

        // May arrive on a system thread: rescheduled on the Dispatcher
        private void OnChanged(object? sender, EventArgs e) =>
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _refreshDebounce.Stop();
                _refreshDebounce.Start();
            }));
    }
}
