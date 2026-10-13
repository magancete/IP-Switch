using IPSwitch.Models;
using IPSwitch.Services;
using Xunit;

namespace IPSwitch.Tests
{
    /// <summary>
    /// Tests of <see cref="NetshCommands"/>: they verify exactly which netsh commands would run,
    /// without administrator rights and without changing the network.
    /// </summary>
    public class NetshCommandsTests
    {
        private static IPProfile StaticProfile() => new()
        {
            Name = "Office",
            Interface = "Ethernet",
            IPAddress = "192.168.1.50",
            SubnetMask = "255.255.255.0",
            Gateway = "192.168.1.1",
            PrimaryDNS = "1.1.1.1",
            SecondaryDNS = "8.8.8.8"
        };

        [Fact]
        public void Dhcp_SetsAddressAndDnsToDhcp()
        {
            var profile = new IPProfile { Name = "Home", Interface = "Wi-Fi" };

            var commands = NetshCommands.BuildApplyCommands(profile);

            Assert.Equal(new[]
            {
                "interface ip set address name=\"Wi-Fi\" source=dhcp",
                "interface ip set dns name=\"Wi-Fi\" source=dhcp"
            }, commands);
        }

        [Fact]
        public void Dhcp_WithStaticDns_SetsBothDnsServers()
        {
            var profile = new IPProfile { Name = "Home", Interface = "Wi-Fi", PrimaryDNS = "1.1.1.1", SecondaryDNS = "1.0.0.1" };

            var commands = NetshCommands.BuildApplyCommands(profile);

            Assert.Equal("interface ip set address name=\"Wi-Fi\" source=dhcp", commands[0]);
            Assert.Equal("interface ip set dns name=\"Wi-Fi\" static 1.1.1.1 validate=no", commands[1]);
            Assert.Equal("interface ip add dns name=\"Wi-Fi\" 1.0.0.1 index=2 validate=no", commands[2]);
        }

        [Fact]
        public void Static_IncludesGatewayWithMetricAndDns()
        {
            var commands = NetshCommands.BuildApplyCommands(StaticProfile());

            Assert.Equal(new[]
            {
                "interface ip set address name=\"Ethernet\" static 192.168.1.50 255.255.255.0 192.168.1.1 1",
                "interface ip set dns name=\"Ethernet\" static 1.1.1.1 validate=no",
                "interface ip add dns name=\"Ethernet\" 8.8.8.8 index=2 validate=no"
            }, commands);
        }

        [Fact]
        public void Static_WithoutGateway_UsesNone()
        {
            var profile = StaticProfile();
            profile.Gateway = NetworkService.Dhcp;
            profile.PrimaryDNS = NetworkService.Dhcp;
            profile.SecondaryDNS = NetworkService.Dhcp;

            var commands = NetshCommands.BuildApplyCommands(profile);

            Assert.Equal("interface ip set address name=\"Ethernet\" static 192.168.1.50 255.255.255.0 none", commands[0]);
            Assert.Equal("interface ip set dns name=\"Ethernet\" source=dhcp", commands[1]);
            Assert.Equal(2, commands.Count);
        }

        [Fact]
        public void Static_UsesCustomGatewayMetric()
        {
            var profile = StaticProfile();
            profile.GatewayMetric = 25;

            Assert.EndsWith("192.168.1.1 25", NetshCommands.BuildApplyCommands(profile)[0]);
        }

        [Fact]
        public void Static_AddsSecondaryAddressesBeforeDns()
        {
            var profile = StaticProfile();
            profile.SecondaryAddresses = new List<string> { "10.0.0.5/24", "172.16.0.9 255.255.0.0" };

            var commands = NetshCommands.BuildApplyCommands(profile);

            Assert.Equal("interface ip add address name=\"Ethernet\" 10.0.0.5 255.255.255.0", commands[1]);
            Assert.Equal("interface ip add address name=\"Ethernet\" 172.16.0.9 255.255.0.0", commands[2]);
            Assert.StartsWith("interface ip set dns", commands[3]);
        }

        [Fact]
        public void Dhcp_IgnoresSecondaryAddresses()
        {
            var profile = new IPProfile { Name = "x", Interface = "Ethernet", SecondaryAddresses = new List<string> { "10.0.0.5/24" } };

            Assert.DoesNotContain(NetshCommands.BuildApplyCommands(profile), c => c.Contains("add address"));
        }

        [Fact]
        public void PrimaryDnsMissing_IgnoresSecondaryDns()
        {
            var profile = StaticProfile();
            profile.PrimaryDNS = NetworkService.Dhcp;

            var commands = NetshCommands.BuildApplyCommands(profile);

            Assert.Contains("interface ip set dns name=\"Ethernet\" source=dhcp", commands);
            Assert.DoesNotContain(commands, c => c.Contains("8.8.8.8"));
        }

        [Fact]
        public void QuoteName_StripsEmbeddedQuotesToPreventArgumentInjection() =>
            Assert.Equal("\"Eth0 static 1.1.1.1\"", NetshCommands.QuoteName("Eth0\" static 1.1.1.1"));

        [Fact]
        public void RestartAdapter_DisablesThenEnables()
        {
            var commands = NetshCommands.BuildRestartAdapterCommands("Ethernet 2");

            Assert.Equal("interface set interface name=\"Ethernet 2\" admin=disabled", commands[0]);
            Assert.Equal("interface set interface name=\"Ethernet 2\" admin=enabled", commands[1]);
        }

        [Theory]
        [InlineData(true, "interface set interface name=\"Wi-Fi\" admin=enabled")]
        [InlineData(false, "interface set interface name=\"Wi-Fi\" admin=disabled")]
        public void SetAdapterEnabled_BuildsAdminCommand(bool enabled, string expected) =>
            Assert.Equal(expected, NetshCommands.BuildSetAdapterEnabledCommand("Wi-Fi", enabled));

        [Theory]
        [InlineData("Bluetooth Network Connection", "Bluetooth Device (PAN)", true)]
        [InlineData("Conexión de área local* 3", "Microsoft Wi-Fi Direct Virtual Adapter", true)]
        [InlineData("Ethernet 3", "Npcap Loopback Adapter", true)]
        [InlineData("Ethernet", "Realtek PCIe GbE Family Controller", false)]
        [InlineData("Wi-Fi", "Intel(R) Wi-Fi 6 AX201", false)]
        public void IsExcludedAdapter_FiltersVirtualAndSystemAdapters(string name, string description, bool excluded) =>
            Assert.Equal(excluded, NetworkService.IsExcludedAdapter(name, description));

        [Fact]
        public void HiddenAdapters_AreCaseInsensitiveAndNotDuplicated()
        {
            var hidden = new List<string>();

            AdapterService.SetHidden(hidden, "VMware Network Adapter VMnet1", hidden: true);
            AdapterService.SetHidden(hidden, "vmware network adapter vmnet1", hidden: true);

            Assert.Single(hidden);
            Assert.True(AdapterService.IsHidden(hidden, "VMWARE NETWORK ADAPTER VMNET1"));
            Assert.False(AdapterService.IsHidden(null, "Ethernet"));

            AdapterService.SetHidden(hidden, "VMware Network Adapter VMnet1", hidden: false);
            Assert.Empty(hidden);
        }

        [Fact]
        public void NullProfile_Throws() =>
            Assert.Throws<ArgumentNullException>(() => NetshCommands.BuildApplyCommands(null!));
    }
}
