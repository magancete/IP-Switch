using System.Windows;

namespace IPSwitch.Services
{
    /// <summary>
    /// Initial size and position of the main window, adapted to the screen (pure functions, testable).
    /// </summary>
    /// <remarks>
    /// The first time, the window takes its preferred size but never more than <see cref="ScreenFraction"/> of the
    /// work area, so it fits small or scaled screens (e.g. 1366 × 768 at 125 %). Afterwards the last size and position
    /// are restored, as long as enough of the window is still visible on the screens (a monitor may have been removed).
    /// </remarks>
    public static class WindowPlacement
    {
        /// <summary>Largest share of the work area used by the default size.</summary>
        public const double ScreenFraction = 0.9;

        /// <summary>Minimum visible part (in DIPs) for a saved position to be reused.</summary>
        private const double MinimumVisible = 120;

        /// <summary>Computes the window bounds.</summary>
        /// <param name="saved">Last bounds, or <see langword="null"/> if none were saved.</param>
        /// <param name="workArea">Work area of the primary screen (without the taskbar).</param>
        /// <param name="virtualScreen">Bounds of all the screens together.</param>
        /// <param name="preferred">Preferred size the first time.</param>
        /// <param name="minimum">Minimum window size.</param>
        /// <returns>The bounds to apply.</returns>
        public static Rect Fit(Rect? saved, Rect workArea, Rect virtualScreen, Size preferred, Size minimum)
        {
            if (saved is Rect last && last.Width > 0 && last.Height > 0)
            {
                var visible = Rect.Intersect(last, virtualScreen);
                if (!visible.IsEmpty && visible.Width >= MinimumVisible && visible.Height >= MinimumVisible)
                {
                    return new Rect(last.X, last.Y,
                        Math.Max(minimum.Width, last.Width),
                        Math.Max(minimum.Height, last.Height));
                }
            }

            double width = Math.Max(minimum.Width, Math.Min(preferred.Width, workArea.Width * ScreenFraction));
            double height = Math.Max(minimum.Height, Math.Min(preferred.Height, workArea.Height * ScreenFraction));
            return new Rect(
                workArea.Left + (workArea.Width - width) / 2,
                workArea.Top + (workArea.Height - height) / 2,
                width, height);
        }
    }
}
