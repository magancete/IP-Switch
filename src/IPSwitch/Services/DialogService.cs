using Wpf.Ui.Controls;

namespace IPSwitch.Services
{
    /// <summary>
    /// Confirmation dialogs with the WPF-UI Fluent style.
    /// </summary>
    /// <remarks>
    /// Every window that shows dialogs must contain a <see cref="ContentDialogHost"/>.
    /// </remarks>
    public static class DialogService
    {
        /// <summary>Shows a confirmation dialog with a primary button and a "Cancel" button.</summary>
        /// <param name="host">Dialog host of the window showing it.</param>
        /// <param name="title">Dialog title.</param>
        /// <param name="message">Body text.</param>
        /// <param name="primaryButton">Text of the confirmation button.</param>
        /// <returns><see langword="true"/> if the user pressed the primary button.</returns>
        public static async Task<bool> ConfirmAsync(ContentDialogHost host, string title, string message, string primaryButton)
        {
            var dialog = new ContentDialog(host)
            {
                Title = title,
                Content = message,
                PrimaryButtonText = primaryButton,
                CloseButtonText = Loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        /// <summary>Shows an information dialog with a single "OK" button.</summary>
        /// <param name="host">Dialog host of the window showing it.</param>
        /// <param name="title">Dialog title.</param>
        /// <param name="message">Body text.</param>
        /// <returns>A task that completes when the dialog is closed.</returns>
        public static async Task ShowMessageAsync(ContentDialogHost host, string title, string message)
        {
            var dialog = new ContentDialog(host)
            {
                Title = title,
                Content = message,
                CloseButtonText = Loc.Get("Common_Ok"),
                DefaultButton = ContentDialogButton.Close
            };

            await dialog.ShowAsync();
        }
    }
}
