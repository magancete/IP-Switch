using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IPSwitch.Converters
{
    /// <summary>
    /// Converts <see langword="true"/> into <see cref="Visibility.Collapsed"/> and <see langword="false"/> into
    /// <see cref="Visibility.Visible"/>: the inverse of <see cref="System.Windows.Controls.BooleanToVisibilityConverter"/>.
    /// </summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public sealed class InverseBooleanToVisibilityConverter : IValueConverter
    {
        /// <summary>Converts the boolean into an inverted visibility.</summary>
        /// <param name="value">Boolean value.</param>
        /// <param name="targetType">Not used.</param>
        /// <param name="parameter">Not used.</param>
        /// <param name="culture">Not used.</param>
        /// <returns><see cref="Visibility.Collapsed"/> if <see langword="true"/>; otherwise <see cref="Visibility.Visible"/>.</returns>
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Converts the visibility back into an inverted boolean.</summary>
        /// <param name="value">Visibility.</param>
        /// <param name="targetType">Not used.</param>
        /// <param name="parameter">Not used.</param>
        /// <param name="culture">Not used.</param>
        /// <returns><see langword="true"/> if not visible.</returns>
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is not Visibility.Visible;
    }
}
