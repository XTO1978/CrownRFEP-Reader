using System.Globalization;

namespace CrownRFEP_Reader.Converters;

/// <summary>
/// Invierte un valor booleano
/// </summary>
public class InvertedBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return value;
    }
}

/// <summary>
/// Convierte un porcentaje (0-100) a un valor de progreso (0-1)
/// </summary>
public class ProgressConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
            return intValue / 100.0;
        if (value is double doubleValue)
            return doubleValue / 100.0;
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double doubleValue)
            return (int)(doubleValue * 100);
        return 0;
    }
}

/// <summary>
/// Convierte un booleano de favorito a emoji
/// </summary>
public class FavoriteConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return boolValue ? "⭐" : "";
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convierte un booleano a visibilidad
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return boolValue;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convierte una duración en segundos (double/int/TimeSpan) a texto mm:ss o h:mm:ss.
/// </summary>
public class DurationToMinutesConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return "0:00";

        TimeSpan ts;
        switch (value)
        {
            case TimeSpan span:
                ts = span;
                break;
            case double d:
                ts = TimeSpan.FromSeconds(d);
                break;
            case float f:
                ts = TimeSpan.FromSeconds(f);
                break;
            case int i:
                ts = TimeSpan.FromSeconds(i);
                break;
            case long l:
                ts = TimeSpan.FromSeconds(l);
                break;
            default:
                return "0:00";
        }

        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";

        return ts.TotalMinutes >= 1
            ? $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}"
            : $"0:{ts.Seconds:D2}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
