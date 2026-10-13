namespace IPSwitch.Models
{
    /// <summary>
    /// Read-only snapshot of a network adapter's state, ready to be shown on the Home page.
    /// </summary>
    /// <remarks>Texts are already localized (or "N/A") so they can be bound directly.</remarks>
    public class NetworkInterfaceDetail
    {
        /// <summary>Adapter name (e.g. <c>Ethernet</c>).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Hardware or driver description.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Whether the adapter is connected.</summary>
        public bool IsUp { get; set; }

        /// <summary>Whether the adapter is enabled in Windows (disabled ones have no IP data).</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>Whether the user hid the adapter (only visible with "Show hidden adapters").</summary>
        public bool IsHidden { get; set; }

        /// <summary>Whether the IP address is obtained via DHCP.</summary>
        public bool IsDhcp { get; set; }

        /// <summary>Localized state ("Connected"/"Disconnected"/"Disabled").</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>MAC address formatted as <c>AA:BB:CC:DD:EE:FF</c>.</summary>
        public string MacAddress { get; set; } = string.Empty;

        /// <summary>Primary IPv4 address.</summary>
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>Subnet mask.</summary>
        public string SubnetMask { get; set; } = string.Empty;

        /// <summary>IPv4 gateway.</summary>
        public string Gateway { get; set; } = string.Empty;

        /// <summary>Comma-separated IPv4 DNS servers.</summary>
        public string DnsServers { get; set; } = string.Empty;

        /// <summary>Localized addressing mode ("DHCP"/"Static").</summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>Link speed with units.</summary>
        public string Speed { get; set; } = string.Empty;

        /// <summary>Type for the icon: <c>WiFi</c> or <c>Ethernet</c>.</summary>
        public string InterfaceType { get; set; } = "Other";
    }
}
