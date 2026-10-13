using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;

namespace IPSwitch.Services
{
    /// <summary>Adapter disabled in Windows (not returned by <see cref="NetworkInterface.GetAllNetworkInterfaces"/>).</summary>
    public sealed class DisabledAdapter
    {
        /// <summary>Creates the adapter description.</summary>
        /// <param name="name">Connection name (e.g. <c>Ethernet 2</c>).</param>
        /// <param name="description">Hardware description.</param>
        public DisabledAdapter(string name, string description)
        {
            Name = name;
            Description = description;
        }

        /// <summary>Connection name.</summary>
        public string Name { get; }

        /// <summary>Hardware or driver description.</summary>
        public string Description { get; }
    }

    /// <summary>
    /// Adapter management: enable/disable in Windows, hide in the application and open the WiFi network list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Windows does not return disabled adapters from <see cref="NetworkInterface.GetAllNetworkInterfaces"/>, so they
    /// are queried through WMI (<c>Win32_NetworkAdapter</c>), which does not depend on the system language.
    /// Enabling or disabling uses <c>netsh interface set interface</c> and requires administrator rights.
    /// </para>
    /// <para>
    /// Hiding is visual only: the adapter keeps working, but it is not shown on Home, the compact view, the tray
    /// flyout or the profile editor. The list is stored in <c>AppSettings.HiddenAdapters</c>.
    /// </para>
    /// </remarks>
    public static class AdapterService
    {
        /// <summary>Raised when the list of hidden adapters changes.</summary>
        public static event EventHandler? HiddenAdaptersChanged;

        /// <summary>Raises <see cref="HiddenAdaptersChanged"/> (call it after changing and saving the list).</summary>
        public static void NotifyHiddenAdaptersChanged() => HiddenAdaptersChanged?.Invoke(null, EventArgs.Empty);

        /// <summary>Lists the disabled adapters that would be useful (Ethernet/WiFi, not system virtual ones).</summary>
        /// <returns>Disabled adapters sorted by name; empty if WMI is not available.</returns>
        public static List<DisabledAdapter> GetDisabledAdapters()
        {
            var result = new List<DisabledAdapter>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT NetConnectionID, Description, NetEnabled FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL");

                foreach (ManagementObject adapter in searcher.Get())
                {
                    using (adapter)
                    {
                        string name = adapter["NetConnectionID"] as string ?? string.Empty;
                        string description = adapter["Description"] as string ?? string.Empty;

                        if (adapter["NetEnabled"] is bool enabled && !enabled &&
                            name.Length > 0 && !NetworkService.IsExcludedAdapter(name, description))
                        {
                            result.Add(new DisabledAdapter(name, description));
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[AdapterService] WMI query failed: {ex.Message}");
            }

            return result.OrderBy(a => a.Name).ToList();
        }

        /// <summary>Enables or disables an adapter in Windows.</summary>
        /// <param name="interfaceName">Adapter name.</param>
        /// <param name="enabled"><see langword="true"/> to enable, <see langword="false"/> to disable.</param>
        /// <returns>The netsh result.</returns>
        public static Task<CommandResult> SetEnabledAsync(string interfaceName, bool enabled) =>
            NetworkService.RunNetshAsync(NetshCommands.BuildSetAdapterEnabledCommand(interfaceName, enabled));

        /// <summary>
        /// Opens the native Windows panel with the available WiFi networks, to search and connect.
        /// If it is not available, opens Settings › Network &amp; Internet › WiFi.
        /// </summary>
        /// <returns><see langword="true"/> if either of them could be opened.</returns>
        public static bool OpenWifiNetworks()
        {
            foreach (string uri in new[] { "ms-availablenetworks:", "ms-settings:network-wifi" })
            {
                try
                {
                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                    return true;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    Debug.WriteLine($"[AdapterService] Could not open {uri}: {ex.Message}");
                }
            }
            return false;
        }

        /// <summary>Whether an adapter is hidden (case-insensitive).</summary>
        /// <param name="hiddenAdapters">List of hidden adapters.</param>
        /// <param name="interfaceName">Adapter name.</param>
        /// <returns><see langword="true"/> if it is hidden.</returns>
        public static bool IsHidden(IEnumerable<string>? hiddenAdapters, string interfaceName) =>
            hiddenAdapters?.Any(h => string.Equals(h, interfaceName, StringComparison.OrdinalIgnoreCase)) == true;

        /// <summary>Hides or shows an adapter again, without duplicates.</summary>
        /// <param name="hiddenAdapters">List of hidden adapters to modify.</param>
        /// <param name="interfaceName">Adapter name.</param>
        /// <param name="hidden"><see langword="true"/> to hide it.</param>
        public static void SetHidden(List<string> hiddenAdapters, string interfaceName, bool hidden)
        {
            hiddenAdapters.RemoveAll(h => string.Equals(h, interfaceName, StringComparison.OrdinalIgnoreCase));
            if (hidden) hiddenAdapters.Add(interfaceName);
        }

        /// <summary>Useful, enabled adapters that are not hidden.</summary>
        /// <param name="hiddenAdapters">List of hidden adapters.</param>
        /// <returns>Visible adapters.</returns>
        public static IEnumerable<NetworkInterface> GetVisibleInterfaces(IEnumerable<string>? hiddenAdapters) =>
            NetworkService.GetUsefulInterfaces().Where(n => !IsHidden(hiddenAdapters, n.Name));
    }
}
