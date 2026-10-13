using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace IPSwitch.Converters
{
    /// <summary>
    /// Converts a color written as text (<c>#RRGGBB</c>) into a frozen <see cref="SolidColorBrush"/>.
    /// </summary>
    /// <remarks>
    /// An empty or invalid string produces <see cref="Brushes.Transparent"/>, avoiding binding errors for profiles
    /// without a color.
    /// </remarks>
    [ValueConversion(typeof(string), typeof(Brush))]
    public sealed class StringToBrushConverter : IValueConverter
    {
        /// <summary>Converts the color text into a brush.</summary>
        /// <param name="value">Color as text.</param>
        /// <param name="targetType">Target type (not used).</param>
        /// <param name="parameter">Not used.</param>
        /// <param name="culture">Not used.</param>
        /// <returns>The matching brush, or transparent.</returns>
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(text));
                    brush.Freeze();
                    return brush;
                }
                catch (FormatException)
                {
                    // Malformed color: treated as "no color"
                }
            }
            return Brushes.Transparent;
        }

        /// <summary>The reverse conversion is not supported.</summary>
        /// <param name="value">Not used.</param>
        /// <param name="targetType">Not used.</param>
        /// <param name="parameter">Not used.</param>
        /// <param name="culture">Not used.</param>
        /// <returns><see cref="DependencyProperty.UnsetValue"/>.</returns>
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            DependencyProperty.UnsetValue;
    }
}
