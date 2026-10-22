using IPSwitch.Models;
using IPSwitch.Services;
using System.Windows;
using Xunit;

namespace IPSwitch.Tests
{
    /// <summary>Tests of additional gateways (parsing, validation, netsh) and of the main window placement.</summary>
    public class GatewaysAndPlacementTests
    {
        [Theory]
        [InlineData("10.0.0.254", "10.0.0.254", 0)]
        [InlineData("10.0.0.254 20", "10.0.0.254", 20)]
        [InlineData(" 10.0.0.254,20 ", "10.0.0.254", 20)]
        [InlineData("10.0.0.254;9999", "10.0.0.254", 9999)]
        public void TryParseGateway_AcceptsAddressWithOptionalMetric(string text, string address, int metric)
        {
            Assert.True(IpUtils.TryParseGateway(text, out string parsedAddress, out int parsedMetric));
            Assert.Equal(address, parsedAddress);
            Assert.Equal(metric, parsedMetric);
        }

        [Theory]
        [InlineData("")]
        [InlineData("10.0.0")]
        [InlineData("10.0.0.254 0")]
        [InlineData("10.0.0.254 10000")]
        [InlineData("10.0.0.254 abc")]
        [InlineData("10.0.0.254 20 30")]
        public void TryParseGateway_RejectsInvalidText(string text) =>
            Assert.False(IpUtils.TryParseGateway(text, out _, out _));

        [Theory]
        [InlineData("10.0.0.254", "10.0.0.254")]
        [InlineData("10.0.0.254,  20", "10.0.0.254 20")]
        public void TryNormalizeGateway_UsesTheCanonicalForm(string text, string expected)
        {
            Assert.True(IpUtils.TryNormalizeGateway(text, out string normalized));
            Assert.Equal(expected, normalized);
        }

        [Fact]
        public void Netsh_AddsEachGatewayAfterTheAddressesAndBeforeDns()
        {
            var profile = new IPProfile
            {
                Name = "Office", Interface = "Ethernet", IPAddress = "10.0.0.10", SubnetMask = "255.255.255.0",
                Gateway = "10.0.0.1", GatewayMetric = 5,
                SecondaryAddresses = new List<string> { "10.0.0.11/24" },
                AdditionalGateways = new List<string> { "10.0.0.2 20", "10.0.0.3" },
                PrimaryDNS = "1.1.1.1"
            };

            var commands = NetshCommands.BuildApplyCommands(profile);

            Assert.Equal(new[]
            {
                "interface ip set address name=\"Ethernet\" static 10.0.0.10 255.255.255.0 10.0.0.1 5",
                "interface ip add address name=\"Ethernet\" 10.0.0.11 255.255.255.0",
                "interface ip add address name=\"Ethernet\" gateway=10.0.0.2 gwmetric=20",
                // Without its own metric, the gateway uses the profile's gateway metric
                "interface ip add address name=\"Ethernet\" gateway=10.0.0.3 gwmetric=5",
                "interface ip set dns name=\"Ethernet\" static 1.1.1.1 validate=no",
            }, commands);
        }

        [Fact]
        public void Validation_RejectsGatewaysWithDhcpOrBadFormat()
        {
            var dhcp = new IPProfile { Name = "x", Interface = "Ethernet", AdditionalGateways = new List<string> { "10.0.0.2" } };
            Assert.Equal("Val_GatewaysDhcp", dhcp.GetValidationErrorKey());

            var bad = new IPProfile
            {
                Name = "x", Interface = "Ethernet", IPAddress = "10.0.0.10", SubnetMask = "255.255.255.0",
                AdditionalGateways = new List<string> { "10.0.0.2 metric" }
            };
            Assert.Equal("Val_GatewaysInvalid", bad.GetValidationErrorKey());

            bad.AdditionalGateways = new List<string> { "10.0.0.2 20" };
            Assert.Null(bad.GetValidationErrorKey());
        }

        [Fact]
        public void Clone_CopiesGatewaysIndependently()
        {
            var original = new IPProfile { AdditionalGateways = new List<string> { "10.0.0.2" } };
            var copy = original.Clone();
            copy.AdditionalGateways.Add("10.0.0.3");

            Assert.Single(original.AdditionalGateways);
        }

        private static readonly Rect SmallScreen = new(0, 0, 1093, 574);      // 1366 x 768 at 125 %, minus taskbar
        private static readonly Rect LargeScreen = new(0, 0, 2560, 1400);
        private static readonly Size Preferred = new(960, 640);
        private static readonly Size Minimum = new(540, 420);

        [Fact]
        public void Placement_FitsTheDefaultSizeIntoSmallScreens()
        {
            var bounds = WindowPlacement.Fit(null, SmallScreen, SmallScreen, Preferred, Minimum);

            Assert.True(bounds.Width <= SmallScreen.Width * WindowPlacement.ScreenFraction);
            Assert.True(bounds.Height <= SmallScreen.Height * WindowPlacement.ScreenFraction);
            Assert.True(bounds.Left >= 0 && bounds.Top >= 0, "Centered inside the work area");
        }

        [Fact]
        public void Placement_UsesThePreferredSizeOnLargeScreens()
        {
            var bounds = WindowPlacement.Fit(null, LargeScreen, LargeScreen, Preferred, Minimum);

            Assert.Equal(Preferred.Width, bounds.Width);
            Assert.Equal(Preferred.Height, bounds.Height);
            Assert.Equal((LargeScreen.Width - Preferred.Width) / 2, bounds.Left);
        }

        [Fact]
        public void Placement_RestoresVisibleBoundsAndIgnoresOffscreenOnes()
        {
            var saved = new Rect(100, 80, 700, 500);
            Assert.Equal(saved, WindowPlacement.Fit(saved, LargeScreen, LargeScreen, Preferred, Minimum));

            // Saved on a monitor that is no longer connected
            var offscreen = new Rect(4000, 100, 700, 500);
            var bounds = WindowPlacement.Fit(offscreen, LargeScreen, LargeScreen, Preferred, Minimum);
            Assert.True(LargeScreen.Contains(bounds));
        }

        [Fact]
        public void Placement_NeverGoesBelowTheMinimumSize()
        {
            var tiny = new Rect(0, 0, 500, 380);
            var bounds = WindowPlacement.Fit(new Rect(10, 10, 200, 150), tiny, tiny, Preferred, Minimum);

            Assert.True(bounds.Width >= Minimum.Width && bounds.Height >= Minimum.Height);
        }
    }
}
