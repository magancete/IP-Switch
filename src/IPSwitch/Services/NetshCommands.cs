using IPSwitch.Models;

namespace IPSwitch.Services
{
    /// <summary>
    /// Builds the <c>netsh</c> arguments needed to apply an <see cref="IPProfile"/>.
    /// </summary>
    /// <remarks>
    /// This is a pure function: it does not run anything, it only builds the ordered list of arguments.
    /// That way the exact commands can be verified by tests without administrator rights.
    /// Execution is handled by <see cref="NetworkService"/>.
    /// </remarks>
    public static class NetshCommands
    {
        /// <summary>
        /// Builds, in execution order, the <c>netsh</c> arguments that apply the profile.
        /// </summary>
        /// <param name="profile">Profile to apply. It must be valid (<see cref="IPProfile.IsValid"/>).</param>
        /// <returns>List of argument strings; each one is a separate <c>netsh</c> invocation.</returns>
        /// <remarks>
        /// Order: primary address (+ gateway), additional addresses and, last, DNS.
        /// With DHCP the additional addresses are ignored (netsh cannot add them to a DHCP interface).
        /// </remarks>
        public static IReadOnlyList<string> BuildApplyCommands(IPProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            string name = QuoteName(profile.Interface);
            var commands = new List<string>();

            if (profile.IsDhcp)
            {
                commands.Add($"interface ip set address name={name} source=dhcp");
            }
            else
            {
                string gateway = NetworkService.IsSet(profile.Gateway)
                    ? $"{profile.Gateway.Trim()} {Math.Max(1, profile.GatewayMetric)}"
                    : "none";

                commands.Add(
                    $"interface ip set address name={name} static {profile.IPAddress.Trim()} {profile.SubnetMask.Trim()} {gateway}");

                foreach (string entry in profile.SecondaryAddresses ?? new List<string>())
                {
                    if (IpUtils.TryParseAddressWithMask(entry, out string address, out string mask))
                        commands.Add($"interface ip add address name={name} {address} {mask}");
                }
            }

            if (!NetworkService.IsSet(profile.PrimaryDNS))
            {
                commands.Add($"interface ip set dns name={name} source=dhcp");
            }
            else
            {
                commands.Add($"interface ip set dns name={name} static {profile.PrimaryDNS.Trim()} validate=no");

                if (NetworkService.IsSet(profile.SecondaryDNS))
                    commands.Add($"interface ip add dns name={name} {profile.SecondaryDNS.Trim()} index=2 validate=no");
            }

            return commands;
        }

        /// <summary>
        /// Builds the arguments to disable and enable an adapter again.
        /// </summary>
        /// <param name="interfaceName">Adapter name.</param>
        /// <returns>Two commands: disable and enable.</returns>
        public static IReadOnlyList<string> BuildRestartAdapterCommands(string interfaceName) => new[]
        {
            BuildSetAdapterEnabledCommand(interfaceName, enabled: false),
            BuildSetAdapterEnabledCommand(interfaceName, enabled: true)
        };

        /// <summary>Builds the argument to enable or disable an adapter.</summary>
        /// <param name="interfaceName">Adapter name.</param>
        /// <param name="enabled"><see langword="true"/> to enable it.</param>
        /// <returns>netsh arguments.</returns>
        public static string BuildSetAdapterEnabledCommand(string interfaceName, bool enabled) =>
            $"interface set interface name={QuoteName(interfaceName)} admin={(enabled ? "enabled" : "disabled")}";

        /// <summary>
        /// Quotes the interface name for netsh, removing embedded quotes that would break the command line
        /// (preventing argument injection).
        /// </summary>
        /// <param name="interfaceName">Adapter name.</param>
        /// <returns>The name between double quotes.</returns>
        public static string QuoteName(string? interfaceName) =>
            "\"" + (interfaceName ?? string.Empty).Replace("\"", string.Empty).Trim() + "\"";
    }
}
