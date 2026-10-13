using IPSwitch.Services;
using Xunit;

namespace IPSwitch.Tests
{
    /// <summary>Tests of <see cref="IpUtils"/>: validation and conversion of IPv4 addresses and masks.</summary>
    public class IpUtilsTests
    {
        [Theory]
        [InlineData("192.168.1.10")]
        [InlineData("10.0.0.1")]
        [InlineData("0.0.0.0")]
        [InlineData("255.255.255.255")]
        [InlineData(" 172.16.0.1 ")]
        public void IsValidIPv4_AcceptsFullDottedAddresses(string input) =>
            Assert.True(IpUtils.IsValidIPv4(input));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("192")]          // IPAddress.TryParse would accept it
        [InlineData("1.2.3")]
        [InlineData("1.2.3.4.5")]
        [InlineData("256.1.1.1")]
        [InlineData("a.b.c.d")]
        [InlineData("1..2.3")]
        [InlineData("::1")]
        [InlineData("1.2.3.4/24")]
        public void IsValidIPv4_RejectsInvalidOrAbbreviated(string? input) =>
            Assert.False(IpUtils.IsValidIPv4(input));

        [Theory]
        [InlineData(0, "0.0.0.0")]
        [InlineData(8, "255.0.0.0")]
        [InlineData(16, "255.255.0.0")]
        [InlineData(23, "255.255.254.0")]
        [InlineData(24, "255.255.255.0")]
        [InlineData(30, "255.255.255.252")]
        [InlineData(32, "255.255.255.255")]
        public void PrefixToMask_ConvertsCorrectly(int prefix, string mask) =>
            Assert.Equal(mask, IpUtils.PrefixToMask(prefix));

        [Theory]
        [InlineData(-1)]
        [InlineData(33)]
        public void PrefixToMask_ThrowsOutOfRange(int prefix) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => IpUtils.PrefixToMask(prefix));

        [Fact]
        public void MaskAndPrefix_RoundTripForAllPrefixes()
        {
            for (int prefix = 0; prefix <= 32; prefix++)
            {
                Assert.True(IpUtils.TryMaskToPrefix(IpUtils.PrefixToMask(prefix), out int result));
                Assert.Equal(prefix, result);
            }
        }

        [Theory]
        [InlineData("255.0.255.0")]
        [InlineData("255.255.255.1")]
        [InlineData("0.255.255.255")]
        [InlineData("192.168.1.1")]
        public void IsValidMask_RejectsNonContiguousMasks(string mask) =>
            Assert.False(IpUtils.IsValidMask(mask));

        [Theory]
        [InlineData("10.0.0.5/24", "10.0.0.5", "255.255.255.0")]
        [InlineData("10.0.0.5 255.255.0.0", "10.0.0.5", "255.255.0.0")]
        [InlineData("  10.0.0.5   255.255.255.128 ", "10.0.0.5", "255.255.255.128")]
        [InlineData("192.168.10.1/32", "192.168.10.1", "255.255.255.255")]
        public void TryParseAddressWithMask_AcceptsCidrAndDecimal(string input, string address, string mask)
        {
            Assert.True(IpUtils.TryParseAddressWithMask(input, out string a, out string m));
            Assert.Equal(address, a);
            Assert.Equal(mask, m);
        }

        [Theory]
        [InlineData("10.0.0.5")]
        [InlineData("10.0.0.5/0")]
        [InlineData("10.0.0.5/33")]
        [InlineData("10.0.0.5/abc")]
        [InlineData("10.0.0/24")]
        [InlineData("10.0.0.5 255.0.255.0")]
        [InlineData("10.0.0.5 255.255.255.0 extra")]
        public void TryParseAddressWithMask_RejectsInvalid(string input) =>
            Assert.False(IpUtils.TryParseAddressWithMask(input, out _, out _));

        [Theory]
        [InlineData("10.0.0.5 255.255.255.0", "10.0.0.5/24")]
        [InlineData("10.0.0.5/16", "10.0.0.5/16")]
        public void TryNormalizeAddressWithMask_ProducesCidr(string input, string expected)
        {
            Assert.True(IpUtils.TryNormalizeAddressWithMask(input, out string normalized));
            Assert.Equal(expected, normalized);
        }
    }
}
