using IPSwitch.Services;
using System.IO;
using Xunit;

namespace IPSwitch.Tests
{
    /// <summary>Tests of the data folder display and migration (<see cref="AppPaths"/>).</summary>
    public class AppPathsTests
    {
        [Theory]
        [InlineData(@"C:\Users\Ana\AppData\Roaming\IPSwitch", @"C:\Users\Ana\AppData\Roaming\IPSwitch")]
        [InlineData(@"c:\users\ana\appdata\roaming\ipswitch\", @"C:\Users\Ana\AppData\Roaming\IPSwitch")]
        public void DefaultFolder_IsShownAsAppDataVariable(string folder, string defaultFolder) =>
            Assert.Equal(@"%AppData%\IPSwitch", AppPaths.FormatForDisplay(folder, defaultFolder));

        [Fact]
        public void OverriddenFolder_IsShownInFull() =>
            Assert.Equal(@"D:\Portable\Data",
                AppPaths.FormatForDisplay(@"D:\Portable\Data", @"C:\Users\Ana\AppData\Roaming\IPSwitch"));

        [Fact]
        public void DefaultDataFolder_IsUnderRoamingAppData() =>
            Assert.Equal(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IPSwitch"),
                AppPaths.DefaultDataFolder);

        [Fact]
        public void Migration_CopiesMissingFilesOnly()
        {
            string root = Path.Combine(Path.GetTempPath(), "ipswitch-tests-" + Guid.NewGuid().ToString("N"));
            string source = Path.Combine(root, "old");
            string target = Path.Combine(root, "new");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
            try
            {
                File.WriteAllText(Path.Combine(source, "profiles.json"), "old-profiles");
                File.WriteAllText(Path.Combine(source, "settings.json"), "old-settings");
                File.WriteAllText(Path.Combine(target, "settings.json"), "current-settings");

                AppPaths.MigrateFrom(source, target);

                Assert.Equal("old-profiles", File.ReadAllText(Path.Combine(target, "profiles.json")));
                Assert.Equal("current-settings", File.ReadAllText(Path.Combine(target, "settings.json")));
                Assert.True(File.Exists(Path.Combine(source, "profiles.json")), "Migration must copy, not move");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
