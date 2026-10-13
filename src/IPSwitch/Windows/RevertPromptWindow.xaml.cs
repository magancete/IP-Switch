using IPSwitch.Services;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace IPSwitch.Windows
{
    /// <summary>
    /// Asks whether to keep a configuration without connectivity, with a countdown that rolls back by default.
    /// </summary>
    /// <remarks>
    /// The safe option (roll back) is the default: it is chosen when pressing Enter or Esc, closing the window or
    /// when the time runs out. So if the change cuts off remote access, the computer recovers by itself.
    /// </remarks>
    public partial class RevertPromptWindow : FluentWindow
    {
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private int _remaining;

        /// <summary>Creates the dialog.</summary>
        /// <param name="profileName">Name of the applied profile.</param>
        /// <param name="seconds">Countdown seconds.</param>
        public RevertPromptWindow(string profileName, int seconds)
        {
            InitializeComponent();
            _remaining = seconds;
            MessageText.Text = Loc.Format("Revert_Message", profileName);
            UpdateCountdown();

            _timer.Tick += (_, _) =>
            {
                _remaining--;
                if (_remaining <= 0)
                {
                    _timer.Stop();
                    DialogResult = false;
                    return;
                }
                UpdateCountdown();
            };
            Loaded += (_, _) => _timer.Start();
            Closed += (_, _) => _timer.Stop();
        }

        /// <summary>Shows the dialog modally.</summary>
        /// <param name="profileName">Name of the applied profile.</param>
        /// <param name="seconds">Countdown seconds.</param>
        /// <returns><see langword="true"/> if the user chose to keep the changes.</returns>
        public static Task<bool> AskAsync(string profileName, int seconds)
        {
            var window = new RevertPromptWindow(profileName, seconds);
            return Task.FromResult(window.ShowDialog() == true);
        }

        private void UpdateCountdown() => CountdownText.Text = Loc.Format("Revert_Countdown", _remaining);

        private void OnKeepClick(object sender, RoutedEventArgs e) => DialogResult = true;

        private void OnRevertClick(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
