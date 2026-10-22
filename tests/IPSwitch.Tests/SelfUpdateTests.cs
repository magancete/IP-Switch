using IPSwitch.Services;
using System.IO;
using Xunit;

namespace IPSwitch.Tests
{
    /// <summary>Tests of the release parsing (<see cref="UpdateService.ParseRelease"/>) and checksums (<see cref="SelfUpdater"/>).</summary>
    public class SelfUpdateTests
    {
        private static readonly Version Current = new(1, 0, 1);

        private const string Release = @"{
            ""tag_name"": ""v1.2.0"",
            ""html_url"": ""https://github.com/magancete/IP-Switch/releases/tag/v1.2.0"",
            ""assets"": [
                { ""name"": ""IPSwitch.exe.sha256"", ""browser_download_url"": ""https://example.test/IPSwitch.exe.sha256"" },
                { ""name"": ""IPSwitch.exe"", ""browser_download_url"": ""https://example.test/IPSwitch.exe"" }
            ]
        }";

        [Fact]
        public void ParseRelease_ReadsVersionPageAndAssets()
        {
            var update = UpdateService.ParseRelease(Release, Current);

            Assert.NotNull(update);
            Assert.Equal(new Version(1, 2, 0), update!.Version);
            Assert.Equal("https://github.com/magancete/IP-Switch/releases/tag/v1.2.0", update.Url);
            Assert.Equal("https://example.test/IPSwitch.exe", update.ExecutableUrl);
            Assert.Equal("https://example.test/IPSwitch.exe.sha256", update.ChecksumUrl);
            Assert.True(update.CanInstall);
        }

        [Fact]
        public void ParseRelease_WithoutChecksum_CannotBeInstalled()
        {
            string json = @"{ ""tag_name"": ""v1.2.0"", ""assets"": [
                { ""name"": ""IPSwitch.exe"", ""browser_download_url"": ""https://example.test/IPSwitch.exe"" } ] }";

            var update = UpdateService.ParseRelease(json, Current);

            Assert.NotNull(update);
            Assert.False(update!.CanInstall);
            Assert.Equal(UpdateService.ReleasesPage, update.Url);
        }

        [Theory]
        [InlineData("v1.0.1")]
        [InlineData("v1.0.0")]
        [InlineData("not-a-version")]
        public void ParseRelease_IgnoresSameOlderOrInvalidVersions(string tag) =>
            Assert.Null(UpdateService.ParseRelease($@"{{ ""tag_name"": ""{tag}"" }}", Current));

        [Theory]
        [InlineData("8e473514eef0e2d72a7c4fd28a1fb55df437b647a577d4e2f374e4e16ba3c498  IPSwitch.exe")]
        [InlineData("8E473514EEF0E2D72A7C4FD28A1FB55DF437B647A577D4E2F374E4E16BA3C498\r\n")]
        public void ParseChecksum_AcceptsSha256sumFormat(string text) =>
            Assert.Equal("8e473514eef0e2d72a7c4fd28a1fb55df437b647a577d4e2f374e4e16ba3c498", SelfUpdater.ParseChecksum(text));

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("not found")]
        [InlineData("8e473514eef0e2d72a7c4fd28a1fb55df437b647a577d4e2f374e4e16ba3c4  too short")]
        public void ParseChecksum_RejectsTextWithoutAHash(string? text) =>
            Assert.Null(SelfUpdater.ParseChecksum(text));

        [Fact]
        public void ComputeSha256_MatchesTheKnownValue()
        {
            string file = Path.GetTempFileName();
            try
            {
                File.WriteAllText(file, "abc");
                Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", SelfUpdater.ComputeSha256(file));
            }
            finally
            {
                File.Delete(file);
            }
        }
    }
}
