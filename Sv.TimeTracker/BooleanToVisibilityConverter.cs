using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Sv.TimeTracker;

public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var invert = string.Equals(parameter as string, "Inverse", StringComparison.OrdinalIgnoreCase);

        var flag = value switch
        {
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            int n => n != 0,
            long l => l != 0,
            not null => true,
            null => false
        };

        if (invert)
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var invert = string.Equals(parameter as string, "Inverse", StringComparison.OrdinalIgnoreCase);
        var visible = value is Visibility.Visible;
        return invert ? !visible : visible;
    }
}

public class HexColorToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush FallbackBrush = CreateFrozenBrush(Color.FromRgb(0x00, 0x78, 0xD4));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                if (ColorConverter.ConvertFromString(hex.Trim()) is Color color)
                {
                    if (parameter is string alphaStr &&
                        double.TryParse(alphaStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity))
                    {
                        color.A = (byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255);
                    }

                    return CreateFrozenBrush(color);
                }
            }
            catch
            {
                // Fall back to default accent brush
            }
        }

        return FallbackBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public class RatioToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var maxWidth = 260.0;
        if (parameter is string paramStr &&
            double.TryParse(paramStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMax))
        {
            maxWidth = parsedMax;
        }

        if (value is double ratio)
        {
            return Math.Max(4.0, Math.Clamp(ratio, 0.0, 1.0) * maxWidth);
        }

        return 4.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
