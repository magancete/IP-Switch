using IPSwitch.Models;
using IPSwitch.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace IPSwitch.Windows
{
    /// <summary>
    /// Compact view of the application: a small window with the quick profile panel.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>Remembers its position in <see cref="AppSettings.CompactLeft"/>/<see cref="AppSettings.CompactTop"/>.</description></item>
    /// <item><description>It is resizable: the panel reflows and the size is stored in <see cref="AppSettings.CompactWidth"/>/<see cref="AppSettings.CompactHeight"/>.</description></item>
    /// <item><description>Its maximum height is exactly what is needed to show every adapter (see <see cref="UpdateMaxHeight"/>).</description></item>
    /// <item><description>Fades when it loses focus if <see cref="AppSettings.CompactFadeWhenInactive"/> is enabled.</description></item>
    /// <item><description>Closing it is the same as closing the application (honoring the tray and confirmation settings).</description></item>
    /// </list>
    /// </remarks>
    public partial class CompactWindow : FluentWindow
    {
        private const double InactiveOpacity = 0.7;

        /// <summary>Lowest height ever allowed: title bar plus one row of the panel (or the empty-state message).</summary>
        private const double MinimumContentHeight = 120;

        private readonly MainWindow _main;
        private readonly AppSettings _settings = App.Settings;

        /// <summary>Creates the compact view attached to the main window.</summary>
        /// <param name="main">Main window, which handles applying profiles and closing.</param>
        public CompactWindow(MainWindow main)
        {
            _main = main;
            InitializeComponent();

            Panel.DataContext = new QuickPanelViewModel(main.ApplyProfileAsync);
            Topmost = _settings.AlwaysOnTop;
            PinButton.IsChecked = Topmost;

            Activated += (_, _) => Opacity = 1;
            Deactivated += (_, _) =>
            {
                if (_settings.CompactFadeWhenInactive && IsVisible) Opacity = InactiveOpacity;
            };
            RestoreSize();
            Loaded += (_, _) => RestorePosition();
            LocationChanged += (_, _) => SavePosition();
            SizeChanged += (_, _) => SaveSize();
        }

        /// <summary>Notification host of this window.</summary>
        public SnackbarPresenter Notifications => SnackbarPresenter;

        /// <summary>Dialog host of this window.</summary>
        public ContentDialogHost Dialogs => DialogHost;

        /// <summary>Syncs the "always on top" state with the settings.</summary>
        public void ApplyTopmost()
        {
            Topmost = _settings.AlwaysOnTop;
            PinButton.IsChecked = Topmost;
        }

        /// <summary>Restores the size chosen by the user; without a saved size the window fits its content.</summary>
        private void RestoreSize()
        {
            if (_settings.CompactWidth is double width && width >= MinWidth)
                Width = width;

            if (_settings.CompactHeight is double height && height >= MinHeight)
            {
                SizeToContent = SizeToContent.Manual;
                Height = height;
            }
        }

        /// <summary>
        /// Recomputes the maximum height when the content changes (adapters appear or disappear) or the visible area
        /// changes (resizing; when narrower, profiles take more rows).
        /// </summary>
        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
                UpdateMaxHeight();
        }

        /// <summary>
        /// Sets <see cref="FrameworkElement.MaxHeight"/> to the exact height that shows every adapter without scrolling
        /// (current height plus what is missing or left over in the scrollable area), within the screen work area.
        /// </summary>
        private void UpdateMaxHeight()
        {
            // Adapters are loaded in the background: until the first snapshot arrives the panel is empty and its
            // height says nothing (computing it then would lock the window at the title bar height)
            if (!IsLoaded || Scroller.ExtentHeight <= 0) return;

            // The viewport can be 0 when the window is shorter than its chrome: the extent is then what is missing
            double contentHeight = ActualHeight + (Scroller.ExtentHeight - Math.Max(0, Scroller.ViewportHeight));
            double max = Math.Min(Math.Ceiling(contentHeight), SystemParameters.WorkArea.Height);
            if (Math.Abs(MaxHeight - max) < 1) return;

            MinHeight = Math.Max(MinimumContentHeight, Math.Min(MinHeight, max));
            MaxHeight = Math.Max(MinHeight, max);
        }

        /// <summary>Stores the size only when the user changes it (not while it fits its content).</summary>
        private void SaveSize()
        {
            if (!IsLoaded || WindowState != WindowState.Normal) return;
            _settings.CompactWidth = ActualWidth;
            if (SizeToContent == SizeToContent.Manual)
                _settings.CompactHeight = ActualHeight;
        }

        private void RestorePosition()
        {
            var screen = SystemParameters.WorkArea;
            if (_settings.CompactLeft is double left && _settings.CompactTop is double top &&
                left >= SystemParameters.VirtualScreenLeft &&
                top >= SystemParameters.VirtualScreenTop &&
                left + 50 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                top + 50 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            {
                Left = left;
                Top = top;
            }
            else
            {
                // By default, in the bottom-right corner of the work area
                Left = screen.Right - ActualWidth - 16;
                Top = screen.Bottom - ActualHeight - 16;
            }
        }

        private void SavePosition()
        {
            if (!IsLoaded || WindowState != WindowState.Normal) return;
            _settings.CompactLeft = Left;
            _settings.CompactTop = Top;
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e) =>
            _ = (Panel.DataContext as QuickPanelViewModel)?.RefreshAsync();

        private void OnPinClick(object sender, RoutedEventArgs e)
        {
            _settings.AlwaysOnTop = PinButton.IsChecked == true;
            _settings.Save();
            _main.ApplyTopmost();
        }

        private void OnExpandClick(object sender, RoutedEventArgs e) => _main.ExitCompactMode();

        private async void OnClosing(object? sender, CancelEventArgs e)
        {
            if (_main.IsExiting) return;

            e.Cancel = true;
            _settings.Save();
            await _main.RequestCloseAsync(Dialogs);
        }
    }
}
