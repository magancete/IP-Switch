using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IPSwitch.Converters
{
    /// <summary>
    /// Computes how many columns fit in an available width, for responsive grids
    /// (e.g. <c>UniformGrid.Columns</c> bound to the container's <c>ActualWidth</c>).
    /// </summary>
    /// <remarks>
    /// <see cref="ColumnWidth"/> is the minimum desired width per column and <see cref="MaxColumns"/> the maximum.
    /// It always returns at least one column.
    /// </remarks>
    [ValueConversion(typeof(double), typeof(int))]
    public sealed class WidthToColumnsConverter : IValueConverter
    {
        /// <summary>Minimum width of each column, in device-independent units.</summary>
        public double ColumnWidth { get; set; } = 200;

        /// <summary>Maximum number of columns.</summary>
        public int MaxColumns { get; set; } = 3;

        /// <summary>Converts a width into a number of columns.</summary>
        /// <param name="value">Available width (<see cref="double"/>).</param>
        /// <param name="targetType">Not used.</param>
        /// <param name="parameter">Not used.</param>
        /// <param name="culture">Not used.</param>
        /// <returns>Between 1 and <see cref="MaxColumns"/>.</returns>
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Calculate(value is double width ? width : 0, ColumnWidth, MaxColumns);

        /// <summary>The reverse conversion is not supported.</summary>
        /// <param name="value">Not used.</param>
        /// <param name="targetType">Not used.</param>
        /// <param name="parameter">Not used.</param>
        /// <param name="culture">Not used.</param>
        /// <returns><see cref="DependencyProperty.UnsetValue"/>.</returns>
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            DependencyProperty.UnsetValue;

        /// <summary>Number of columns of at least <paramref name="columnWidth"/> that fit in <paramref name="width"/>.</summary>
        /// <param name="width">Available width.</param>
        /// <param name="columnWidth">Minimum width per column.</param>
        /// <param name="maxColumns">Maximum number of columns.</param>
        /// <returns>Between 1 and <paramref name="maxColumns"/>.</returns>
        public static int Calculate(double width, double columnWidth, int maxColumns)
        {
            if (double.IsNaN(width) || width <= 0 || columnWidth <= 0) return Math.Max(1, maxColumns);
            return Math.Max(1, Math.Min(maxColumns, (int)(width / columnWidth)));
        }
    }
}
