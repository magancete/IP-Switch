using IPSwitch.Services;
using Xunit;

namespace IPSwitch.Tests
{
    /// <summary>Tests of version parsing and comparison in <see cref="UpdateService"/>, and of the responsive column calculation.</summary>
    public class UpdateServiceTests
    {
        [Theory]
        [InlineData("v1.2.3", 1, 2, 3)]
        [InlineData("1.2.3", 1, 2, 3)]
        [InlineData("V2.0", 2, 0, 0)]
        [InlineData("v3", 3, 0, 0)]
        [InlineData("v1.4.0-beta.1", 1, 4, 0)]
        [InlineData("1.0.0+build.5", 1, 0, 0)]
        public void TryParseVersion_AcceptsCommonTagFormats(string tag, int major, int minor, int build)
        {
            Assert.True(UpdateService.TryParseVersion(tag, out var version));
            Assert.Equal(new Version(major, minor, build), version);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("latest")]
        [InlineData("v")]
        public void TryParseVersion_RejectsInvalidTags(string? tag) =>
            Assert.False(UpdateService.TryParseVersion(tag, out _));

        [Theory]
        [InlineData("1.0.1", "1.0.0", true)]
        [InlineData("1.1.0", "1.0.9", true)]
        [InlineData("2.0.0", "1.9.9", true)]
        [InlineData("1.0.0", "1.0.0", false)]
        [InlineData("0.9.0", "1.0.0", false)]
        public void IsNewer_ComparesVersions(string candidate, string current, bool expected) =>
            Assert.Equal(expected, UpdateService.IsNewer(Version.Parse(candidate), Version.Parse(current)));

        [Theory]
        [InlineData(900, 3)]
        [InlineData(600, 3)]
        [InlineData(500, 2)]
        [InlineData(379, 1)]
        [InlineData(0, 3)]          // Not measured yet: the maximum is used
        [InlineData(double.NaN, 3)]
        public void WidthToColumns_AdaptsToAvailableWidth(double width, int expected) =>
            Assert.Equal(expected, Converters.WidthToColumnsConverter.Calculate(width, 190, 3));

        [Fact]
        public void CurrentVersion_HasThreeComponents()
        {
            var version = UpdateService.CurrentVersion;
            Assert.True(version.Build >= 0);
            Assert.Equal(-1, version.Revision);
        }
    }
}
