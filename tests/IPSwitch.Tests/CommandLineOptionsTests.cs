using IPSwitch.Services;
using Xunit;

namespace IPSwitch.Tests
{
    /// <summary>Tests of command-line parsing (<see cref="CommandLineOptions"/>).</summary>
    public class CommandLineOptionsTests
    {
        [Fact]
        public void NoArguments_StartsNormally()
        {
            var options = CommandLineOptions.Parse(Array.Empty<string>());

            Assert.False(options.IsHeadless);
            Assert.False(options.Minimized);
            Assert.False(options.Compact);
            Assert.Null(options.Error);
        }

        [Fact]
        public void Apply_TakesProfileNameAndIsHeadless()
        {
            var options = CommandLineOptions.Parse(new[] { "--apply", "Office LAN" });

            Assert.Equal("Office LAN", options.ApplyProfileName);
            Assert.True(options.IsHeadless);
        }

        [Fact]
        public void Apply_IsCaseInsensitive() =>
            Assert.Equal("x", CommandLineOptions.Parse(new[] { "--APPLY", "x" }).ApplyProfileName);

        [Theory]
        [InlineData]
        [InlineData("  ")]
        public void Apply_WithoutName_IsError(params string[] extra)
        {
            var options = CommandLineOptions.Parse(new[] { "--apply" }.Concat(extra).ToArray());

            Assert.NotNull(options.Error);
            Assert.True(options.IsHeadless);
        }

        [Fact]
        public void MinimizedAndCompact_AreFlags()
        {
            var options = CommandLineOptions.Parse(new[] { "--minimized", "--compact" });

            Assert.True(options.Minimized);
            Assert.True(options.Compact);
            Assert.False(options.IsHeadless);
        }

        [Theory]
        [InlineData("--help")]
        [InlineData("-h")]
        [InlineData("/?")]
        public void Help_IsRecognized(string arg) =>
            Assert.True(CommandLineOptions.Parse(new[] { arg }).ShowHelp);

        [Fact]
        public void UnknownArgument_IsError() =>
            Assert.Contains("--bogus", CommandLineOptions.Parse(new[] { "--bogus" }).Error);

        [Fact]
        public void BuildApplyArguments_QuotesAndRoundTrips()
        {
            string args = CommandLineOptions.BuildApplyArguments("My \"Office\"");

            Assert.Equal("--apply \"My Office\"", args);
        }
    }
}
