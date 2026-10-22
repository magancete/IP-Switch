using IPSwitch.Services;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Xunit;

namespace IPSwitch.Tests
{
    /// <summary>Tests of the startup task definition (<see cref="StartupService"/>) and argument quoting (<see cref="Elevation"/>).</summary>
    public class StartupAndElevationTests
    {
        private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        private const string Sid = "S-1-5-21-1000-1000-1000-1001";

        [Fact]
        public void TaskXml_KeepsTheTrayAppRunning()
        {
            var settings = XDocument.Parse(StartupService.BuildTaskXml(@"C:\Tools\IPSwitch.exe", Sid))
                .Descendants(Ns + "Settings").Single();

            // The schtasks /SC ONLOGON defaults would stop the app after 72 h and skip it on battery power
            Assert.Equal("PT0S", settings.Element(Ns + "ExecutionTimeLimit")!.Value);
            Assert.Equal("false", settings.Element(Ns + "DisallowStartIfOnBatteries")!.Value);
            Assert.Equal("false", settings.Element(Ns + "StopIfGoingOnBatteries")!.Value);
            Assert.Equal("IgnoreNew", settings.Element(Ns + "MultipleInstancesPolicy")!.Value);
        }

        [Fact]
        public void TaskXml_StartsElevatedAndMinimizedAtTheUsersSignIn()
        {
            var task = XDocument.Parse(StartupService.BuildTaskXml(@"C:\Tools\IPSwitch.exe", Sid));

            Assert.Equal(Sid, task.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "UserId")!.Value);
            Assert.Equal("HighestAvailable", task.Descendants(Ns + "RunLevel").Single().Value);
            Assert.Equal("InteractiveToken", task.Descendants(Ns + "LogonType").Single().Value);
            Assert.Equal(CommandLineOptions.MinimizedArgument, task.Descendants(Ns + "Arguments").Single().Value);
        }

        [Theory]
        [InlineData(@"C:\Tools\IPSwitch.exe")]
        [InlineData(@"D:\Projects\Visual Studio\IP & Switch\IPSwitch.exe")]
        [InlineData(@"C:\Users\Ana Núñez\Escritorio\<IPSwitch>.exe")]
        public void TaskXml_RoundTripsTheExecutablePath(string path) =>
            Assert.Equal(path, StartupService.ParseTaskCommand(StartupService.BuildTaskXml(path, Sid)));

        [Fact]
        public void ParseTaskCommand_StripsQuotesAndRejectsGarbage()
        {
            string xml = $"<Task xmlns=\"{Ns}\"><Actions><Exec><Command>\"C:\\A B\\IPSwitch.exe\"</Command></Exec></Actions></Task>";

            Assert.Equal(@"C:\A B\IPSwitch.exe", StartupService.ParseTaskCommand(xml));
            Assert.Null(StartupService.ParseTaskCommand("ERROR: The system cannot find the file specified."));
        }

        [Theory]
        [InlineData(new object[] { new string[0] })]
        [InlineData(new object[] { new[] { "--minimized" } })]
        [InlineData(new object[] { new[] { "--apply", "Office" } })]
        [InlineData(new object[] { new[] { "--apply", "Home (DHCP)" } })]
        [InlineData(new object[] { new[] { "--apply", "Say \"hi\"" } })]
        [InlineData(new object[] { new[] { "--apply", @"C:\path with space\" } })]
        [InlineData(new object[] { new[] { "--apply", "" } })]
        public void JoinArguments_IsSplitBackIntoTheSameArguments(string[] arguments)
        {
            // Windows splits the command line with CommandLineToArgvW; a dummy program name comes first
            string commandLine = "IPSwitch.exe " + Elevation.JoinArguments(arguments);

            Assert.Equal(arguments, SplitCommandLine(commandLine).Skip(1).ToArray());
        }

        [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        private static string[] SplitCommandLine(string commandLine)
        {
            IntPtr argv = CommandLineToArgvW(commandLine, out int count);
            try
            {
                return Enumerable.Range(0, count)
                    .Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!)
                    .ToArray();
            }
            finally
            {
                LocalFree(argv);
            }
        }
    }
}
