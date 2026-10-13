using IPSwitch.Services;
using Xunit;

namespace IPSwitch.Tests
{
    /// <summary>Tests of the feedback entry point (<see cref="IssueReporter"/>).</summary>
    public class IssueReporterTests
    {
        [Fact]
        public void ChooseUrl_PointsToTheTemplateChooser() =>
            Assert.Equal("https://github.com/magancete/IP-Switch/issues/new/choose", IssueReporter.ChooseUrl);

        [Fact]
        public void Summary_ContainsVersionWindowsAndLanguage()
        {
            string summary = IssueReporter.BuildSummary(new Dictionary<string, string>
            {
                ["version"] = "1.0.0",
                ["os"] = "Windows 11 Pro 23H2 (22631.4317)",
                ["language"] = "es"
            });

            Assert.Equal("IP Switch 1.0.0 · Windows 11 Pro 23H2 (22631.4317) · Language: es", summary);
        }

        [Fact]
        public void EnvironmentFields_NeverIncludeNetworkData()
        {
            var fields = IssueReporter.GetEnvironmentFields();

            Assert.Equal(new[] { "language", "os", "version" }, fields.Keys.OrderBy(k => k));
            Assert.All(fields.Values, v => Assert.DoesNotMatch(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", v));
        }

        [Fact]
        public void WindowsVersion_IsReadable() =>
            Assert.StartsWith("Windows", IssueReporter.GetWindowsVersion());
    }
}
