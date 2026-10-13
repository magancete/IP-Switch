using IPSwitch.Services;
using Xunit;

namespace IPSwitch.Tests
{
    /// <summary>Tests of the command protocol between launches (<see cref="SingleInstance"/>).</summary>
    public class SingleInstanceTests
    {
        [Fact]
        public void ApplyCommand_RoundTrips()
        {
            string command = SingleInstance.BuildApplyCommand("Office LAN");

            Assert.Equal("apply\tOffice LAN", command);
            Assert.True(SingleInstance.TryParseApplyCommand(command, out string name));
            Assert.Equal("Office LAN", name);
        }

        [Fact]
        public void ApplyCommand_RemovesSeparatorsFromTheName()
        {
            string command = SingleInstance.BuildApplyCommand("Bad\tName\r\nX");

            Assert.True(SingleInstance.TryParseApplyCommand(command, out string name));
            Assert.Equal("Bad Name  X", name);
            Assert.DoesNotContain("\n", command);
        }

        [Theory]
        [InlineData("show")]
        [InlineData("apply")]
        [InlineData("apply\t")]
        [InlineData("apply Office")]
        [InlineData("")]
        public void TryParseApplyCommand_RejectsOtherCommands(string command) =>
            Assert.False(SingleInstance.TryParseApplyCommand(command, out _));

        [Fact]
        public void MutexIsMachineWide() =>
            Assert.StartsWith(@"Global\", SingleInstance.MutexName);
    }
}
