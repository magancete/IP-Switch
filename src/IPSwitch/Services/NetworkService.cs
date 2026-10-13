using IPSwitch.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace IPSwitch.Services
{
    /// <summary>
    /// Result of running one or more system commands.
    /// </summary>
    public sealed class CommandResult
    {
        /// <summary>Creates a result.</summary>
        /// <param name="success">Whether the operation completed successfully.</param>
        /// <param name="message">Command error output, or empty on success.</param>
        public CommandResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        /// <summary>Whether the operation completed successfully.</summary>
        public bool Success { get; }

        /// <summary>Command error output (localized by Windows), or an empty string on success.</summary>
        public string Message { get; }

        /// <summary>Success result without a message.</summary>
        public static readonly CommandResult Ok = new(true, string.Empty);
    }

    /// <summary>
    /// Access to the Windows network adapters: state queries and configuration changes through
    /// <c>netsh</c> and <c>ipconfig</c>.
    /// </summary>
    /// <remarks>
    /// Operations that change the configuration require administrator rights (guaranteed by <c>app.manifest</c>).
    /// Every write operation is asynchronous and does not block the UI. Adapter queries can take several hundred
    /// milliseconds on machines with many virtual adapters: callers on the UI thread should use <c>Task.Run</c>.
    /// </remarks>
    public static class NetworkService
    {
        /// <summary>Sentinel value that means "obtain automatically" in <see cref="IPProfile"/>.</summary>
        public const string Dhcp = "DHCP";

        /// <summary>Timeout of each individual ping, in milliseconds.</summary>
        private const int PingTimeoutMs = 1000;

        private const string TcpipInterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";

        /// <summary>
        /// Name or description fragments of virtual or system adapters that must not be shown.
        /// </summary>
        private static readonly string[] ForbiddenKeywords =
        {
            "WFP", "FORTINET", "NPCAP", "QOS", "VIRTUAL WIFI", "FILTER",
            "BLUETOOTH", "DEBUGGER", "WAN MINIPORT", "CONEXIÓN DE ÁREA LOCAL*", "LOCAL AREA CONNECTION*"
        };

        #region Adapter snapshot

        /// <summary>How long an adapter snapshot is reused by the views.</summary>
        private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(2);

        private static readonly object SnapshotLock = new();
        private static NetworkInterface[]? _snapshot;
        private static DateTime _snapshotTime;
        private static int _snapshotGeneration;

        static NetworkService()
        {
            NetworkChange.NetworkAddressChanged += (_, _) => InvalidateSnapshot();
            NetworkChange.NetworkAvailabilityChanged += (_, _) => InvalidateSnapshot();
        }

        /// <summary>
        /// Every adapter, from a snapshot shared by the views (home, compact view, tray, active-profile detection).
        /// </summary>
        /// <returns>The adapters; the array must not be modified.</returns>
        /// <remarks>
        /// <see cref="NetworkInterface.GetAllNetworkInterfaces"/> takes ~400 ms on machines with many virtual adapters
        /// and several views ask for it after the same network change. The snapshot lives
        /// <see cref="SnapshotLifetime"/> at most and is discarded on any network change and after every
        /// <c>netsh</c>/<c>ipconfig</c> run. Operations that must see the real current state (capturing the
        /// configuration before applying a profile) use <see cref="FindInterface"/>, which never uses it.
        /// </remarks>
        public static NetworkInterface[] GetAllInterfaces()
        {
            int generation;
            lock (SnapshotLock)
            {
                if (_snapshot != null && DateTime.UtcNow - _snapshotTime < SnapshotLifetime)
                    return _snapshot;
                generation = _snapshotGeneration;
            }

            var fresh = NetworkInterface.GetAllNetworkInterfaces();

            lock (SnapshotLock)
            {
                // A change during the enumeration makes it stale: it is returned but not kept
                if (generation == _snapshotGeneration)
                {
                    _snapshot = fresh;
                    _snapshotTime = DateTime.UtcNow;
                }
            }

            return fresh;
        }

        /// <summary>Discards the adapter snapshot (the next query reads the real state).</summary>
        public static void InvalidateSnapshot()
        {
            lock (SnapshotLock)
            {
                _snapshot = null;
                _snapshotGeneration++;
            }
        }

        #endregion

        #region Adapter queries

        /// <summary>Sorted names of the useful physical adapters (Ethernet and WiFi).</summary>
        /// <returns>List of distinct names.</returns>
        public static List<string> GetNetworkInterfaces() =>
            GetUsefulInterfaces()
                .Select(nic => nic.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

        /// <summary>Ethernet/WiFi adapters that are present, excluding virtual and system ones.</summary>
        /// <returns>Sequence of adapters.</returns>
        public static IEnumerable<NetworkInterface> GetUsefulInterfaces() =>
            GetAllInterfaces().Where(IsUsefulInterface);

        /// <summary>Finds an adapter by name (case-insensitive), always reading its current state.</summary>
        /// <param name="interfaceName">Adapter name.</param>
        /// <returns>The adapter, or <see langword="null"/> if it does not exist.</returns>
        public static NetworkInterface? FindInterface(string interfaceName) =>
            NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase));

        private static bool IsUsefulInterface(NetworkInterface nic)
        {
            if (nic.OperationalStatus == OperationalStatus.NotPresent ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                return false;

            var type = nic.NetworkInterfaceType;
            bool isCorrectType = type is NetworkInterfaceType.Ethernet
                                      or NetworkInterfaceType.Wireless80211
                                      or NetworkInterfaceType.GigabitEthernet;

            return isCorrectType && !IsExcludedAdapter(nic.Name, nic.Description);
        }

        /// <summary>
        /// Whether an adapter is virtual or a system adapter (filters, Bluetooth, WAN Miniport…) and must not be shown.
        /// </summary>
        /// <param name="name">Connection name.</param>
        /// <param name="description">Hardware description.</param>
        /// <returns><see langword="true"/> if it must be excluded.</returns>
        public static bool IsExcludedAdapter(string? name, string? description)
        {
            string upperName = (name ?? string.Empty).ToUpperInvariant();
            string upperDesc = (description ?? string.Empty).ToUpperInvariant();
            return ForbiddenKeywords.Any(k => upperName.Contains(k) || upperDesc.Contains(k));
        }

        /// <summary>Whether a profile field has an explicit value (not empty and not <see cref="Dhcp"/>).</summary>
        /// <param name="value">Field value.</param>
        /// <returns><see langword="true"/> if a value is configured.</returns>
        public static bool IsSet(string? value) => !string.IsNullOrWhiteSpace(value) && value != Dhcp;

        /// <summary>First IPv4 address of the adapter, or <see langword="null"/>.</summary>
        /// <param name="nic">Adapter.</param>
        /// <returns>The address as text.</returns>
        public static string? GetIPv4Address(NetworkInterface nic)
        {
            try
            {
                return nic.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?
                    .Address.ToString();
            }
            catch (NetworkInformationException)
            {
                return null;
            }
        }

        /// <summary>Current IPv4 gateway of the adapter, or <see langword="null"/>.</summary>
        /// <param name="interfaceName">Adapter name.</param>
        /// <returns>The gateway as text.</returns>
        public static string? GetGateway(string interfaceName)
        {
            try
            {
                return FindInterface(interfaceName)?.GetIPProperties().GatewayAddresses
                    .Select(g => g.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().Equals("0.0.0.0"))?
                    .ToString();
            }
            catch (NetworkInformationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Captures the current IPv4 configuration of an adapter as a (nameless) profile.
        /// </summary>
        /// <param name="interfaceName">Adapter name.</param>
        /// <returns>The equivalent profile, or <see langword="null"/> if the adapter does not exist or has no IPv4.</returns>
        /// <remarks>
        /// Used to "save the current configuration as a profile" and as the snapshot for rollback.
        /// To tell static DNS servers from DHCP-provided ones, the <c>NameServer</c> registry value is read.
        /// </remarks>
        public static IPProfile? CaptureCurrentConfig(string interfaceName)
        {
            var nic = FindInterface(interfaceName);
            if (nic == null || !nic.Supports(NetworkInterfaceComponent.IPv4)) return null;

            try
            {
                var props = nic.GetIPProperties();
                bool isDhcp = props.GetIPv4Properties()?.IsDhcpEnabled == true;

                var profile = new IPProfile { Interface = nic.Name };

                if (!isDhcp)
                {
                    var addresses = props.UnicastAddresses
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                        .ToList();

                    if (addresses.Count == 0) return null;

                    profile.IPAddress = addresses[0].Address.ToString();
                    profile.SubnetMask = addresses[0].IPv4Mask?.ToString() ?? "255.255.255.0";
                    profile.SecondaryAddresses = addresses.Skip(1)
                        .Select(a => IpUtils.TryNormalizeAddressWithMask($"{a.Address} {a.IPv4Mask}", out var n) ? n : null)
                        .Where(n => n != null)
                        .Cast<string>()
                        .ToList();
                    profile.Gateway = GetGateway(nic.Name) ?? Dhcp;
                }

                if (HasStaticDns(nic.Id))
                {
                    var dns = props.DnsAddresses
                        .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                        .Select(d => d.ToString())
                        .ToList();

                    if (dns.Count > 0) profile.PrimaryDNS = dns[0];
                    if (dns.Count > 1) profile.SecondaryDNS = dns[1];
                }

                return profile;
            }
            catch (NetworkInformationException)
            {
                return null;
            }
        }

        private static bool HasStaticDns(string interfaceId)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(TcpipInterfacesKey + interfaceId);
                return !string.IsNullOrWhiteSpace(key?.GetValue("NameServer") as string);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetworkService] Registry read failed: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Configuration changes

        /// <summary>
        /// Applies a profile by running the commands of <see cref="NetshCommands.BuildApplyCommands"/> in order.
        /// </summary>
        /// <param name="profile">Profile to apply.</param>
        /// <returns>The result; if a command fails, execution stops and its error output is returned.</returns>
        public static Task<CommandResult> ApplyProfile(IPProfile profile) => Task.Run(() =>
        {
            try
            {
                foreach (string command in NetshCommands.BuildApplyCommands(profile))
                {
                    var result = RunCommand("netsh", command);
                    if (!result.Success) return result;
                }
                return CommandResult.Ok;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetworkService] Error: {ex.Message}");
                return new CommandResult(false, ex.Message);
            }
        });

        /// <summary>Releases and renews the adapter's DHCP lease (<c>ipconfig /renew</c>).</summary>
        /// <param name="interfaceName">Adapter name.</param>
        /// <returns>The command result.</returns>
        public static Task<CommandResult> RenewDhcpAsync(string interfaceName) =>
            Task.Run(() => RunCommand("ipconfig", $"/renew {NetshCommands.QuoteName(interfaceName)}"));

        /// <summary>Flushes the DNS resolver cache (<c>ipconfig /flushdns</c>).</summary>
        /// <returns>The command result.</returns>
        public static Task<CommandResult> FlushDnsAsync() =>
            Task.Run(() => RunCommand("ipconfig", "/flushdns"));

        /// <summary>Runs a single <c>netsh</c> command on a background thread.</summary>
        /// <param name="arguments">netsh arguments.</param>
        /// <returns>The command result.</returns>
        public static Task<CommandResult> RunNetshAsync(string arguments) =>
            Task.Run(() => RunCommand("netsh", arguments));

        /// <summary>Disables and enables the adapter again.</summary>
        /// <param name="interfaceName">Adapter name.</param>
        /// <returns>The result; if enabling fails, its error is returned.</returns>
        public static Task<CommandResult> RestartAdapterAsync(string interfaceName) => Task.Run(() =>
        {
            var commands = NetshCommands.BuildRestartAdapterCommands(interfaceName);
            var disable = RunCommand("netsh", commands[0]);
            if (!disable.Success) return disable;

            Thread.Sleep(1500);
            return RunCommand("netsh", commands[1]);
        });

        /// <summary>
        /// Runs a hidden console program and returns its result.
        /// </summary>
        /// <param name="fileName">Executable (e.g. <c>netsh</c>).</param>
        /// <param name="arguments">Arguments.</param>
        /// <returns>Success if the exit code is 0; otherwise the program output.</returns>
        private static CommandResult RunCommand(string fileName, string arguments)
        {
            // Console tools write using the OEM code page (e.g. 850 for Spanish)
            var oemEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = oemEncoding,
                    StandardErrorEncoding = oemEncoding
                });

                if (process == null)
                    return new CommandResult(false, $"{fileName} could not be started.");

                // stderr is read asynchronously to avoid a deadlock if both buffers fill up
                var errorTask = process.StandardError.ReadToEndAsync();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                output = (output + errorTask.Result).Trim();

                Debug.WriteLine($"[{fileName}] {arguments} -> {process.ExitCode}: {output}");
                return process.ExitCode == 0 ? CommandResult.Ok : new CommandResult(false, output);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{fileName}] {ex.Message}");
                return new CommandResult(false, ex.Message);
            }
            finally
            {
                // The command may have changed the configuration: no view may keep showing the previous state
                InvalidateSnapshot();
            }
        }

        #endregion

        #region Connectivity

        /// <summary>Sends an ICMP ping to a host.</summary>
        /// <param name="host">IP address or host name.</param>
        /// <returns><see langword="true"/> if it answered.</returns>
        public static async Task<bool> PingAsync(string host)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, PingTimeoutMs).ConfigureAwait(false);
                return reply.Status == IPStatus.Success;
            }
            catch (Exception ex) when (ex is PingException or InvalidOperationException or ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Waits for connectivity after applying a profile, pinging periodically.
        /// </summary>
        /// <param name="profile">Profile just applied.</param>
        /// <param name="customHost">Host to check; if empty, the gateway is used.</param>
        /// <param name="timeout">Maximum wait time.</param>
        /// <returns>
        /// <see langword="true"/> if a reply was received, or if there is nothing to check
        /// (static IP without gateway and without a custom host).
        /// </returns>
        public static async Task<bool> WaitForConnectivityAsync(IPProfile profile, string? customHost, TimeSpan timeout)
        {
            bool hasCustomHost = !string.IsNullOrWhiteSpace(customHost);
            if (!hasCustomHost && !profile.IsDhcp && !IsSet(profile.Gateway))
                return true;

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                // With DHCP the gateway appears once the lease is obtained
                string? target = hasCustomHost
                    ? customHost!.Trim()
                    : IsSet(profile.Gateway) ? profile.Gateway : GetGateway(profile.Interface);

                if (target != null && await PingAsync(target).ConfigureAwait(false))
                    return true;

                await Task.Delay(1000).ConfigureAwait(false);
            }
            return false;
        }

        #endregion
    }
}
