using IPSwitch.Models;
using IPSwitch.Services;
using IPSwitch.ViewModels;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace IPSwitch.Windows;

/// <summary>
/// Modal window to create or edit a profile.
/// </summary>
/// <remarks>
/// If the user saves, <see cref="Window.DialogResult"/> is <see langword="true"/> and the resulting profile is in
/// <see cref="ResultProfile"/>. Logic and validation live in <see cref="AddEditWindowViewModel"/>.
/// </remarks>
public partial class AddEditWindow : FluentWindow
{
    /// <summary>Creates the editor.</summary>
    /// <param name="existingProfiles">Existing profiles, to validate duplicates.</param>
    /// <param name="profile">Profile to edit, or template; <see langword="null"/> for an empty one.</param>
    /// <param name="isNew">Whether the result will be a new profile instead of replacing <paramref name="profile"/>.</param>
    public AddEditWindow(List<IPProfile> existingProfiles, IPProfile? profile, bool isNew)
    {
        ViewModel = new AddEditWindowViewModel(existingProfiles, profile, isNew);
        ViewModel.Saved += (_, result) =>
        {
            ResultProfile = result;
            DialogResult = true;
        };

        DataContext = ViewModel;
        InitializeComponent();
        Topmost = App.Settings.AlwaysOnTop;
        FitToWorkArea();
        AdvancedExpander.IsExpanded = ViewModel.HasAdvancedValues;
        Loaded += (_, _) => NameBox.Focus();
    }

    /// <summary>Form ViewModel.</summary>
    public AddEditWindowViewModel ViewModel { get; }

    /// <summary>Resulting profile if saved; <see langword="null"/> if cancelled.</summary>
    public IPProfile? ResultProfile { get; private set; }

    /// <summary>
    /// Keeps the dialog inside the screen work area on small or scaled displays: narrower if needed (down to
    /// <see cref="FrameworkElement.MinWidth"/>) and never taller than the work area (the content scrolls).
    /// </summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width - 32));
        MaxHeight = area.Height - 32;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>
    /// Captures the key combination pressed in the hotkey field.
    /// Backspace, Delete or Esc (without modifiers) clear the hotkey; Tab navigates normally.
    /// </summary>
    private void OnHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift) return;

        e.Handled = true;

        if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete or Key.Escape)
        {
            ViewModel.Hotkey = string.Empty;
            return;
        }

        if (HotkeyService.Format(modifiers, key) is { } text)
            ViewModel.Hotkey = text;
    }

    private void OnClearHotkeyClick(object sender, RoutedEventArgs e) => ViewModel.Hotkey = string.Empty;

    private void OnUseCurrentSsidClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.UseCurrentSsid())
            SsidHint.Text = Loc.Get("Edit_NoWifiConnected");
    }
}
