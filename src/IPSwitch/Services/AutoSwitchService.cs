using IPSwitch.Models;
using System.Net.NetworkInformation;
using System.Windows.Threading;

namespace IPSwitch.Services
{
    /// <summary>
    /// Automatically applies a profile when its WiFi adapter connects to the network set in
    /// <see cref="IPProfile.AutoApplySsid"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Listens to the system network change events, coalesces them (debounce) and compares the current SSID of each
    /// adapter with the last one seen. It only acts when the SSID <b>changes</b>, so applying a profile (which raises
    /// network events in turn) does not cause loops.
    /// </para>
    /// <para>Every call to <c>apply</c> happens on the UI thread.</para>
    /// </remarks>
    public sealed class AutoSwitchService : IDisposable
    {
        private readonly Func<IPProfile, Task> _apply;
        private readonly DispatcherTimer _debounce;
        private readonly Dictionary<string, string> _lastSsids = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Creates the service.</summary>
        /// <param name="apply">Function that applies a profile (with notifications and verification).</param>
        public AutoSwitchService(Func<IPProfile, Task> apply)
        {
            _apply = apply;
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _debounce.Tick += async (_, _) =>
            {
                _debounce.Stop();
                await EvaluateAsync();
            };
        }

        /// <summary>Starts listening to network changes and evaluates the current state.</summary>
        public void Start()
        {
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
            _debounce.Start();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
            _debounce.Stop();
        }

        // Events arrive on system threads: they are forwarded to the timer's Dispatcher
        private void OnNetworkChanged(object? sender, EventArgs e) =>
            _debounce.Dispatcher.BeginInvoke(new Action(() =>
            {
                _debounce.Stop();
                _debounce.Start();
            }));

        /// <summary>Compares the current SSIDs with the last ones seen and applies the matching profile.</summary>
        /// <returns>A task that completes when the required profiles have been applied.</returns>
        public async Task EvaluateAsync()
        {
            var current = await Task.Run(WlanApi.GetConnectedSsids);

            // Forget disconnected adapters so the profile is applied again on reconnection
            foreach (string gone in _lastSsids.Keys.Where(k => !current.ContainsKey(k)).ToList())
                _lastSsids.Remove(gone);

            foreach (var pair in current)
            {
                if (_lastSsids.TryGetValue(pair.Key, out string? previous) && previous == pair.Value)
                    continue;

                _lastSsids[pair.Key] = pair.Value;

                var profile = FindProfile(ProfileService.Profiles, pair.Key, pair.Value);
                if (profile != null && !profile.IsActive)
                    await _apply(profile);
            }
        }

        /// <summary>
        /// Finds the valid profile configured to be applied for an SSID on an adapter.
        /// </summary>
        /// <param name="profiles">Available profiles.</param>
        /// <param name="interfaceName">WiFi adapter name.</param>
        /// <param name="ssid">Connected SSID (case-sensitive, like the SSID itself).</param>
        /// <returns>The first matching profile, or <see langword="null"/>.</returns>
        public static IPProfile? FindProfile(IEnumerable<IPProfile> profiles, string interfaceName, string ssid) =>
            profiles.FirstOrDefault(p =>
                p.IsValid &&
                !string.IsNullOrEmpty(p.AutoApplySsid) &&
                p.AutoApplySsid == ssid &&
                string.Equals(p.Interface, interfaceName, StringComparison.OrdinalIgnoreCase));
    }
}
