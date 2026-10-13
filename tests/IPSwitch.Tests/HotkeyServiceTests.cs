using IPSwitch.Services;
using System.Windows.Input;
using Xunit;

namespace IPSwitch.Tests
{
    /// <summary>Tests of the hotkey text format of <see cref="HotkeyService"/>.</summary>
    public class HotkeyServiceTests
    {
        private const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8;

        [Theory]
        [InlineData("Ctrl+Alt+1", ModControl | ModAlt, 0x31)]
        [InlineData("ctrl+shift+F5", ModControl | ModShift, 0x74)]
        [InlineData("Win+P", ModWin, 0x50)]
        [InlineData("Alt + Shift + Z", ModAlt | ModShift, 0x5A)]
        [InlineData("Control+0", ModControl, 0x30)]
        public void TryParse_ValidHotkeys(string text, uint modifiers, uint virtualKey)
        {
            Assert.True(HotkeyService.TryParse(text, out uint m, out uint vk));
            Assert.Equal(modifiers, m);
            Assert.Equal(virtualKey, vk);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("A")]              // No modifier
        [InlineData("Ctrl")]           // Modifier only
        [InlineData("Ctrl+")]
        [InlineData("Ctrl+Alt")]       // The last token is a modifier, not a key
        [InlineData("Hyper+A")]        // Unknown modifier
        [InlineData("Ctrl+NotAKey")]
        [InlineData("Ctrl+65")]        // Numeric enum value
        public void TryParse_InvalidHotkeys(string? text) =>
            Assert.False(HotkeyService.TryParse(text, out _, out _));

        [Theory]
        [InlineData(ModifierKeys.Control | ModifierKeys.Alt, Key.D1, "Ctrl+Alt+1")]
        [InlineData(ModifierKeys.Control | ModifierKeys.Shift, Key.F5, "Ctrl+Shift+F5")]
        [InlineData(ModifierKeys.Windows, Key.P, "Win+P")]
        public void Format_ProducesCanonicalText(ModifierKeys modifiers, Key key, string expected) =>
            Assert.Equal(expected, HotkeyService.Format(modifiers, key));

        [Theory]
        [InlineData(ModifierKeys.None, Key.A)]
        [InlineData(ModifierKeys.Control, Key.LeftCtrl)]
        [InlineData(ModifierKeys.Alt, Key.System)]
        public void Format_ReturnsNullForIncompleteCombinations(ModifierKeys modifiers, Key key) =>
            Assert.Null(HotkeyService.Format(modifiers, key));

        [Fact]
        public void FormatThenParse_RoundTrips()
        {
            string? text = HotkeyService.Format(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.D9);
            Assert.NotNull(text);
            Assert.True(HotkeyService.TryParse(text, out _, out _));
        }
    }
}
