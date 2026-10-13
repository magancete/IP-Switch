using IPSwitch.Models;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace IPSwitch.Services
{
    /// <summary>
    /// Registers global hotkeys (working even when the application does not have focus) that apply profiles.
    /// </summary>
    /// <remarks>
    /// Uses the Win32 <c>RegisterHotKey</c> API on the main window HWND and listens for <c>WM_HOTKEY</c>.
    /// The text format of a hotkey is <c>Mod+Mod+Key</c>, e.g. <c>Ctrl+Alt+1</c> or <c>Ctrl+Shift+F5</c>.
    /// At least one modifier is required so regular keys are never hijacked.
    /// </remarks>
    public sealed class HotkeyService : IDisposable
    {
        private const int WmHotkey = 0x0312;
        private const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8, ModNoRepeat = 0x4000;

        private readonly HwndSource _source;
        private readonly IntPtr _handle;
        private readonly Action<IPProfile> _onHotkey;
        private readonly Dictionary<int, IPProfile> _registered = new();
        private int _nextId = 1;

        /// <summary>Creates the service attached to a window HWND.</summary>
        /// <param name="window">Owner window; its HWND is created if it does not exist yet.</param>
        /// <param name="onHotkey">Action run (on the UI thread) when a profile's hotkey is pressed.</param>
        public HotkeyService(Window window, Action<IPProfile> onHotkey)
        {
            _onHotkey = onHotkey;
            _handle = new WindowInteropHelper(window).EnsureHandle();
            _source = HwndSource.FromHwnd(_handle);
            _source.AddHook(WndProc);
        }

        /// <summary>
        /// Replaces every registered hotkey with the ones of the given profiles.
        /// </summary>
        /// <param name="profiles">Profiles; those without a hotkey or with a malformed one are ignored.</param>
        /// <returns>Profiles whose hotkey could not be registered (usually because another application uses it).</returns>
        public IReadOnlyList<IPProfile> RegisterAll(IEnumerable<IPProfile> profiles)
        {
            UnregisterAll();
            var failed = new List<IPProfile>();

            foreach (var profile in profiles.Where(p => !string.IsNullOrEmpty(p.Hotkey)))
            {
                if (!TryParse(profile.Hotkey, out uint modifiers, out uint virtualKey))
                {
                    failed.Add(profile);
                    continue;
                }

                int id = _nextId++;
                if (RegisterHotKey(_handle, id, modifiers | ModNoRepeat, virtualKey))
                    _registered[id] = profile;
                else
                    failed.Add(profile);
            }

            return failed;
        }

        /// <summary>Unregisters every hotkey.</summary>
        public void UnregisterAll()
        {
            foreach (int id in _registered.Keys)
                UnregisterHotKey(_handle, id);
            _registered.Clear();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            UnregisterAll();
            _source.RemoveHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotkey && _registered.TryGetValue(wParam.ToInt32(), out var profile))
            {
                handled = true;
                _onHotkey(profile);
            }
            return IntPtr.Zero;
        }

        #region Hotkey format

        /// <summary>
        /// Parses a hotkey written as text.
        /// </summary>
        /// <param name="text">Hotkey, e.g. <c>Ctrl+Alt+1</c>. Case-insensitive.</param>
        /// <param name="modifiers">Win32 modifier mask (<c>MOD_*</c>).</param>
        /// <param name="virtualKey">Win32 virtual-key code.</param>
        /// <returns><see langword="true"/> if the format is valid and has at least one modifier.</returns>
        public static bool TryParse(string? text, out uint modifiers, out uint virtualKey)
        {
            modifiers = 0;
            virtualKey = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var tokens = text!.Split('+').Select(t => t.Trim()).ToArray();
            if (tokens.Length < 2 || tokens.Any(t => t.Length == 0)) return false;

            foreach (string token in tokens.Take(tokens.Length - 1))
            {
                switch (token.ToUpperInvariant())
                {
                    case "CTRL": case "CONTROL": modifiers |= ModControl; break;
                    case "ALT": modifiers |= ModAlt; break;
                    case "SHIFT": modifiers |= ModShift; break;
                    case "WIN": modifiers |= ModWin; break;
                    default: return false;
                }
            }

            if (!TryParseKey(tokens[tokens.Length - 1], out Key key)) return false;

            virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
            return modifiers != 0 && virtualKey != 0;
        }

        /// <summary>
        /// Converts a combination captured from the keyboard into canonical text, e.g. <c>Ctrl+Alt+1</c>.
        /// </summary>
        /// <param name="modifiers">Pressed modifiers.</param>
        /// <param name="key">Main key.</param>
        /// <returns>
        /// The hotkey text, or <see langword="null"/> if there are no modifiers or the key is only a modifier.
        /// </returns>
        public static string? Format(ModifierKeys modifiers, Key key)
        {
            if (modifiers == ModifierKeys.None || IsModifierKey(key)) return null;

            var parts = new List<string>();
            if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

            parts.Add(key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString() : key.ToString());
            return string.Join("+", parts);
        }

        private static bool TryParseKey(string token, out Key key)
        {
            key = Key.None;

            // Digits are written "1" but the WPF key is named "D1"
            if (token.Length == 1 && char.IsDigit(token[0]))
                token = "D" + token;

            // Numeric values ("65") that Enum.TryParse would accept are rejected
            return !token.All(char.IsDigit) &&
                   Enum.TryParse(token, ignoreCase: true, out key) &&
                   Enum.IsDefined(typeof(Key), key) &&
                   key != Key.None && !IsModifierKey(key);
        }

        private static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt
            or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

        #endregion

        #region Win32

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        #endregion
    }
}
