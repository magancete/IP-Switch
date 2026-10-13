using IPSwitch.ViewModels;
using System.Windows;
using Wpf.Ui.Controls;

namespace IPSwitch.Windows
{
    /// <summary>
    /// Flyout shown when clicking the tray icon, with the quick profile panel.
    /// </summary>
    /// <remarks>
    /// It is placed in the bottom-right corner of the work area (where the notification area usually is)
    /// and hides automatically when it loses focus, like the native Windows flyouts.
    /// </remarks>
    public partial class TrayFlyoutWindow : FluentWindow
    {
        private const double ScreenMargin = 12;

        private readonly MainWindow _main;
        private DateTime _hiddenAt = DateTime.MinValue;

        /// <summary>Creates the flyout attached to the main window.</summary>
        /// <param name="main">Main window, which handles applying profiles.</param>
        public TrayFlyoutWindow(MainWindow main)
        {
            _main = main;
            InitializeComponent();
            Panel.DataContext = new QuickPanelViewModel(main.ApplyProfileAsync);

            Deactivated += (_, _) => HideFlyout();
            SizeChanged += (_, _) => PositionNearTray();
        }

        /// <summary>Shows the flyout if it is hidden, or hides it if it is visible.</summary>
        public void Toggle()
        {
            // Clicking the icon takes focus away from the flyout (hiding it) just before arriving here:
            // if it has just been hidden, that same click must leave it closed.
            if (IsVisible || (DateTime.UtcNow - _hiddenAt).TotalMilliseconds < 300)
            {
                HideFlyout();
                return;
            }

            Refresh();
            Show();
            PositionNearTray();
            Activate();
        }

        private void Refresh() => _ = (Panel.DataContext as QuickPanelViewModel)?.RefreshAsync();

        private void HideFlyout()
        {
            if (!IsVisible) return;
            Hide();
            _hiddenAt = DateTime.UtcNow;
        }

        private void PositionNearTray()
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - ScreenMargin;
            Top = area.Bottom - ActualHeight - ScreenMargin;
        }

        private void OnOpenClick(object sender, RoutedEventArgs e)
        {
            HideFlyout();
            _main.ShowFromTray();
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e) => Refresh();
    }
}
