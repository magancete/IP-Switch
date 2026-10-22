using IPSwitch.Services;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace IPSwitch.Tests
{
    /// <summary>Tests of the repair of profile shortcuts after moving the executable (<see cref="ShortcutService"/>).</summary>
    public class ShortcutRepairTests
    {
        private const string Current = @"D:\Tools\IPSwitch.exe";

        [Theory]
        [InlineData(@"C:\Old\IPSwitch.exe", "--apply \"Office\"", false, true)]       // moved: repair
        [InlineData(@"C:\Old\IPSwitch.exe", "--apply \"Office\"", true, false)]       // other copy still there: leave it
        [InlineData(@"C:\Old\IPSwitch.exe", "--minimized", false, false)]             // not a profile shortcut
        [InlineData(@"C:\Old\notepad.exe", "--apply \"Office\"", false, false)]       // another program
        [InlineData(Current, "--apply \"Office\"", false, false)]                     // already correct
        [InlineData("", "--apply \"Office\"", false, false)]
        public void NeedsRepair_OnlyForMissingIpSwitchApplyShortcuts(string target, string arguments, bool exists, bool expected) =>
            Assert.Equal(expected, ShortcutService.NeedsRepair(target, arguments, Current, exists));

        [Fact]
        public void RepairShortcuts_PointsBrokenShortcutsToTheExecutable()
        {
            string folder = Path.Combine(Path.GetTempPath(), "ipswitch-lnk-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                // The "new location" must exist; the old one must not
                string executable = Path.Combine(folder, "new", "IPSwitch.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
                File.WriteAllText(executable, string.Empty);

                string broken = Path.Combine(folder, "IP Switch - Office.lnk");
                string unrelated = Path.Combine(folder, "Notepad.lnk");
                CreateLink(broken, Path.Combine(folder, "old", "IPSwitch.exe"), "--apply \"Office\"");
                CreateLink(unrelated, Path.Combine(folder, "old", "notepad.exe"), string.Empty);

                Assert.Equal(1, ShortcutService.RepairShortcuts(folder, executable));
                Assert.Equal(executable, ReadTarget(broken), ignoreCase: true);
                Assert.Equal(Path.Combine(folder, "old", "notepad.exe"), ReadTarget(unrelated), ignoreCase: true);

                // Idempotent: nothing left to repair
                Assert.Equal(0, ShortcutService.RepairShortcuts(folder, executable));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        private static void CreateLink(string path, string target, string arguments) =>
            WithLink(path, (type, link) =>
            {
                type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
                type.InvokeMember("Arguments", BindingFlags.SetProperty, null, link, new object[] { arguments });
                type.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                return null;
            });

        private static string? ReadTarget(string path) =>
            WithLink(path, (type, link) => type.InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null) as string);

        private static string? WithLink(string path, Func<Type, object, string?> action)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
            object shell = Activator.CreateInstance(shellType)!;
            object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path })!;
            try
            {
                return action(link.GetType(), link);
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
