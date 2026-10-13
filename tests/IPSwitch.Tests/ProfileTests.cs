using IPSwitch.Models;
using IPSwitch.Services;
using System.IO;
using Xunit;

namespace IPSwitch.Tests
{
    /// <summary>
    /// Tests of <see cref="IPProfile"/> validation and copying, import/export (<see cref="ProfileTransferService"/>),
    /// profile selection for WiFi auto-switch (<see cref="AutoSwitchService"/>) and compatibility with old files
    /// (<see cref="JsonStore"/>).
    /// </summary>
    public class ProfileTests : IDisposable
    {
        private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), "IPSwitchTests_" + Guid.NewGuid().ToString("N"));

        public ProfileTests() => Directory.CreateDirectory(_tempFolder);

        public void Dispose() => Directory.Delete(_tempFolder, recursive: true);

        private static IPProfile Valid() => new()
        {
            Name = "Office",
            Interface = "Ethernet",
            IPAddress = "192.168.1.50",
            SubnetMask = "255.255.255.0",
            Gateway = "192.168.1.1"
        };

        #region Validation

        [Fact]
        public void ValidStaticAndDhcpProfiles_HaveNoErrors()
        {
            Assert.True(Valid().IsValid);
            Assert.True(new IPProfile { Name = "Home", Interface = "Wi-Fi" }.IsValid);
        }

        [Theory]
        [InlineData("Name", "", "Val_NameRequired")]
        [InlineData("Interface", "", "Val_InterfaceRequired")]
        [InlineData("IPAddress", "192", "Val_IpInvalid")]
        [InlineData("SubnetMask", "255.0.255.0", "Val_MaskInvalid")]
        [InlineData("Gateway", "1.2.3", "Val_IpInvalid")]
        [InlineData("PrimaryDNS", "dns.local", "Val_IpInvalid")]
        [InlineData("Hotkey", "A", "Val_HotkeyInvalid")]
        public void InvalidField_ReportsExpectedKey(string property, string value, string expectedKey)
        {
            var profile = Valid();
            typeof(IPProfile).GetProperty(property)!.SetValue(profile, value);

            Assert.Equal(expectedKey, profile.GetValidationErrorKey());
            Assert.False(profile.IsValid);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(10000)]
        public void MetricOutOfRange_IsInvalid(int metric)
        {
            var profile = Valid();
            profile.GatewayMetric = metric;
            Assert.Equal("Val_MetricInvalid", profile.GetValidationErrorKey());
        }

        [Fact]
        public void SecondaryAddresses_RequireStaticIpAndValidFormat()
        {
            var dhcp = new IPProfile { Name = "x", Interface = "Ethernet", SecondaryAddresses = new List<string> { "10.0.0.5/24" } };
            Assert.Equal("Val_SecondaryDhcp", dhcp.GetValidationErrorKey());

            var bad = Valid();
            bad.SecondaryAddresses = new List<string> { "10.0.0.5" };
            Assert.Equal("Val_SecondaryInvalid", bad.GetValidationErrorKey());
        }

        [Fact]
        public void ValidationMessage_FallsBackToKeyWithoutWpfApplication() =>
            Assert.Equal("Val_NameRequired", new IPProfile { Interface = "x" }.ValidationMessage);

        #endregion

        #region Copia

        [Fact]
        public void Clone_CopiesAllValuesAndIsIndependent()
        {
            var original = Valid();
            original.SecondaryAddresses = new List<string> { "10.0.0.5/24" };
            original.Notes = "n";
            original.Color = "#3498DB";
            original.Hotkey = "Ctrl+Alt+1";
            original.AutoApplySsid = "Office-WiFi";
            original.GatewayMetric = 5;
            original.IsActive = true;

            var copy = original.Clone("Copy");
            copy.SecondaryAddresses.Add("10.0.0.6/24");

            Assert.Equal("Copy", copy.Name);
            Assert.False(copy.IsActive);
            Assert.Equal(original.IPAddress, copy.IPAddress);
            Assert.Equal(original.Hotkey, copy.Hotkey);
            Assert.Equal(original.AutoApplySsid, copy.AutoApplySsid);
            Assert.Equal(5, copy.GatewayMetric);
            Assert.Single(original.SecondaryAddresses);
        }

        #endregion

        #region Importar / exportar

        [Fact]
        public void ExportThenRead_RoundTripsAllFields()
        {
            var profile = Valid();
            profile.SecondaryAddresses = new List<string> { "10.0.0.5/24" };
            profile.Notes = "Planta 2";
            profile.Color = "#E74C3C";
            profile.IsActive = true;
            string path = Path.Combine(_tempFolder, "export.json");

            ProfileTransferService.Export(path, new[] { profile });
            var read = ProfileTransferService.Read(path);

            var result = Assert.Single(read);
            Assert.Equal("Office", result.Name);
            Assert.Equal("Planta 2", result.Notes);
            Assert.Equal("#E74C3C", result.Color);
            Assert.Equal(new[] { "10.0.0.5/24" }, result.SecondaryAddresses);
            Assert.False(result.IsActive);
            Assert.DoesNotContain("IsValid", File.ReadAllText(path));
        }

        [Fact]
        public void Read_InvalidFile_ThrowsInvalidData()
        {
            string path = Path.Combine(_tempFolder, "bad.json");
            File.WriteAllText(path, "{ not json");

            Assert.Throws<InvalidDataException>(() => ProfileTransferService.Read(path));
        }

        [Fact]
        public void Merge_RenamesDuplicatesAndDropsRepeatedHotkeys()
        {
            var existing = new List<IPProfile> { Valid() };
            existing[0].Hotkey = "Ctrl+Alt+1";

            var imported = Valid();
            imported.Hotkey = "Ctrl+Alt+1";
            var second = Valid();

            int added = ProfileTransferService.Merge(existing, new[] { imported, second });

            Assert.Equal(2, added);
            Assert.Equal(new[] { "Office", "Office (2)", "Office (3)" }, existing.Select(p => p.Name));
            Assert.Equal(string.Empty, existing[1].Hotkey);
        }

        [Fact]
        public void GetUniqueName_IsCaseInsensitive() =>
            Assert.Equal("office (2)", ProfileTransferService.GetUniqueName(new[] { Valid() }, "office"));

        #endregion

        #region Compatibility and WiFi auto-switch

        [Fact]
        public void LegacyJsonWithoutNewFields_LoadsWithDefaults()
        {
            // Format written by previous versions (System.Text.Json, without the new fields)
            string path = Path.Combine(_tempFolder, "profiles.json");
            File.WriteAllText(path,
                "[{\"Name\":\"Old\",\"IPAddress\":\"DHCP\",\"SubnetMask\":\"DHCP\",\"Gateway\":\"DHCP\"," +
                "\"PrimaryDNS\":\"DHCP\",\"SecondaryDNS\":\"DHCP\",\"Interface\":\"Ethernet\",\"IsActive\":true}]");

            var profiles = JsonStore.Load<List<IPProfile>>(path);

            var profile = Assert.Single(profiles!);
            Assert.Equal("Old", profile.Name);
            Assert.Equal(1, profile.GatewayMetric);
            Assert.Empty(profile.SecondaryAddresses);
            Assert.True(profile.IsValid);
        }

        [Fact]
        public void JsonStore_SaveThenLoad_RoundTrips()
        {
            string path = Path.Combine(_tempFolder, "sub", "settings.json");
            JsonStore.Save(path, new AppSettings { Language = "es", RevertTimeoutSeconds = 30, CompactLeft = 12.5 });
            JsonStore.Save(path, new AppSettings { Language = "en", RevertTimeoutSeconds = 30, CompactLeft = 12.5 });

            var loaded = JsonStore.Load<AppSettings>(path);

            Assert.Equal("en", loaded!.Language);
            Assert.Equal(30, loaded.RevertTimeoutSeconds);
            Assert.Equal(12.5, loaded.CompactLeft);
            Assert.Null(loaded.CompactTop);
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void JsonStore_CorruptFile_ReturnsNull()
        {
            string path = Path.Combine(_tempFolder, "corrupt.json");
            File.WriteAllText(path, "{{{");
            Assert.Null(JsonStore.Load<AppSettings>(path));
        }

        [Fact]
        public void AutoSwitch_FindsProfileBySsidAndInterface()
        {
            var office = Valid();
            office.Interface = "Wi-Fi";
            office.AutoApplySsid = "Office";
            var invalid = new IPProfile { Name = "", Interface = "Wi-Fi", AutoApplySsid = "Lab" };

            var profiles = new[] { office, invalid };

            Assert.Same(office, AutoSwitchService.FindProfile(profiles, "wi-fi", "Office"));
            Assert.Null(AutoSwitchService.FindProfile(profiles, "Wi-Fi", "office")); // SSIDs are case-sensitive
            Assert.Null(AutoSwitchService.FindProfile(profiles, "Ethernet", "Office"));
            Assert.Null(AutoSwitchService.FindProfile(profiles, "Wi-Fi", "Lab"));   // Invalid profile
        }

        #endregion
    }
}
