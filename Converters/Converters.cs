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
/// Convierte un valor numérico a un valor de progreso (0-1)
/// El parámetro indica el valor máximo (por defecto 100)
/// </summary>
public class ProgressConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double maxValue = 100.0;
        if (parameter is string paramStr && double.TryParse(paramStr, out var parsed))
            maxValue = parsed;
        else if (parameter is int paramInt)
            maxValue = paramInt;
        else if (parameter is double paramDouble)
            maxValue = paramDouble;

        if (value is int intValue)
            return intValue / maxValue;
        if (value is double doubleValue)
            return doubleValue / maxValue;
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double maxValue = 100.0;
        if (parameter is string paramStr && double.TryParse(paramStr, out var parsed))
            maxValue = parsed;

        if (value is double doubleValue)
            return (int)(doubleValue * maxValue);
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

/// <summary>
/// Convierte un booleano a Color para indicar selección.
/// Soporta parámetro con formato "trueColor|falseColor" (ej: "#FF6DDDFF|#FF2A2A2A" o "Black|White")
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;

        // Si hay parámetro con formato "trueColor|falseColor"
        if (parameter is string param && param.Contains('|'))
        {
            var colors = param.Split('|');
            if (colors.Length == 2)
            {
                var colorStr = boolValue ? colors[0] : colors[1];
                try
                {
                    return Color.FromArgb(colorStr);
                }
                catch
                {
                    // Intentar parsear como nombre de color
                    if (colorStr.Equals("Black", StringComparison.OrdinalIgnoreCase))
                        return Colors.Black;
                    if (colorStr.Equals("White", StringComparison.OrdinalIgnoreCase))
                        return Colors.White;
                    return Color.FromArgb(colorStr.StartsWith("#") ? colorStr : "#" + colorStr);
                }
            }
        }

        // Comportamiento por defecto
        if (boolValue)
            return Color.FromArgb("#FF3A5A8A"); // Color de selección activa (azul oscuro)
        return Colors.Transparent; // Transparente cuando no seleccionado
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convierte un booleano a icono de expandir/colapsar
/// </summary>
public class BoolToExpandIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue)
            return "▲"; // Colapsado (flecha arriba)
        return "▼"; // Expandir (flecha abajo)
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convierte un booleano a opacidad (1.0 si true, 0.4 si false).
/// Útil para indicar elementos deshabilitados visualmente.
/// Soporta parámetro con formato "trueOpacity|falseOpacity" (ej: "1.0|0.3")
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;

        // Si hay parámetro con formato "trueOpacity|falseOpacity"
        if (parameter is string param && param.Contains('|'))
        {
            var opacities = param.Split('|');
            if (opacities.Length == 2)
            {
                if (double.TryParse(boolValue ? opacities[0] : opacities[1], 
                    System.Globalization.NumberStyles.Any, 
                    System.Globalization.CultureInfo.InvariantCulture, 
                    out var opacity))
                {
                    return opacity;
                }
            }
        }

        // Comportamiento por defecto
        return boolValue ? 1.0 : 0.4;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter para resaltar el item seleccionado en una lista de opciones.
/// Compara el valor del item con la propiedad del ViewModel indicada en ConverterParameter.
/// </summary>
public class SelectedItemColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Este converter necesita acceso al ViewModel, pero en MAUI no es sencillo desde un converter.
        // Usaremos una lógica simplificada: si el valor coincide con la selección, color activo.
        // La lógica real se hará mediante binding MultiBinding o en el ViewModel.
        return Color.FromArgb("#FF3A3A3A"); // Color por defecto
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Devuelve true si el valor no es null
/// </summary>
public class IsNotNullConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Devuelve color verde para archivos .crown, blanco para otros
/// </summary>
public class CrownFileColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isCrownFile && isCrownFile)
            return Color.FromArgb("#FF4CAF50"); // Verde para archivos .crown
        return Colors.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Devuelve Bold para archivos .crown, None para otros
/// </summary>
public class CrownFileFontConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isCrownFile && isCrownFile)
            return FontAttributes.Bold;
        return FontAttributes.None;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Devuelve true si el valor es 0
/// </summary>
public class IsZeroConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            int intValue => intValue == 0,
            double doubleValue => doubleValue == 0,
            float floatValue => floatValue == 0,
            long longValue => longValue == 0,
            _ => false
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convierte el estado de selección de un tag (int: 0 o 1) a color de fondo
/// </summary>
public class TagSelectedColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isSelected = value switch
        {
            int intValue => intValue == 1,
            bool boolValue => boolValue,
            _ => false
        };
        
        return isSelected 
            ? Color.FromArgb("#FF66BB6A")  // Verde cuando está seleccionado
            : Color.FromArgb("#FF3A3A3A"); // Gris oscuro cuando no está seleccionado
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convierte el estado de selección de un tag de evento a color de fondo (naranja)
/// </summary>
public class EventTagSelectedColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isSelected = value switch
        {
            int intValue => intValue == 1,
            bool boolValue => boolValue,
            _ => false
        };
        
        return isSelected 
            ? Color.FromArgb("#FFFF7043")  // Naranja cuando está seleccionado
            : Color.FromArgb("#FF3A3A3A"); // Gris oscuro cuando no está seleccionado
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convierte una posición normalizada (0..1) a un Rect para AbsoluteLayout con PositionProportional.
/// Útil para colocar marcadores en una línea temporal.
/// </summary>
public class NormalizedPositionToRectConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var x = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            _ => 0.0
        };

        // clamp 0..1
        if (x < 0) x = 0;
        if (x > 1) x = 1;

        // AutoSize (-1) para que el contenido (badge) determine su tamaño
        // y=0.5 -> centrado vertical dentro del contenedor
        return new Rect(x, 0.5, -1, -1);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convierte una posición normalizada (0..1) a un Rect para un tick pequeño (2px) en AbsoluteLayout.
/// </summary>
public class NormalizedPositionToTickRectConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var x = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            _ => 0.0
        };

        if (x < 0) x = 0;
        if (x > 1) x = 1;

        // Tick centrado verticalmente dentro del contenedor
        return new Rect(x, 0.5, 2, 8);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convierte un booleano a FontAttributes (Bold si es true, None si es false)
/// </summary>
public class BoolToFontAttributesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return FontAttributes.Bold;
        return FontAttributes.None;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convierte un int a bool (true si > 0, false si = 0)
/// Parámetro "invert" para invertir la lógica
/// </summary>
public class IntToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = 0;
        if (value is int i) count = i;
        else if (value is long l) count = (int)l;
        
        var result = count > 0;
        
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            result = !result;
            
        return result;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convierte un int a color basado en igualdad con un parámetro
/// Formato del parámetro: "valor:colorSiIgual:colorSiDiferente"
/// </summary>
public class IntEqualityToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int currentValue || parameter is not string param)
            return Colors.Transparent;

        var parts = param.Split(':');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var targetValue))
            return Colors.Transparent;

        var colorIfEqual = parts[1];
        var colorIfDifferent = parts[2];

        return Color.FromArgb(currentValue == targetValue ? colorIfEqual : colorIfDifferent);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convierte un timestamp Unix (long) a fecha legible
/// </summary>
public class TimestampToDateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long timestamp && timestamp > 0)
        {
            var date = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
            return date.ToString("dd/MM/yyyy");
        }
        return "-";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// MultiValueConverter que retorna true solo si todos los valores son true
/// </summary>
public class AllTrueMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length == 0)
            return false;

        foreach (var value in values)
        {
            if (value is bool boolValue && !boolValue)
                return false;
            if (value is not bool)
                return false;
        }
        return true;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter que devuelve un texto diferente según el valor booleano
/// Uso: ConverterParameter='TextoTrue|TextoFalse'
/// </summary>
public class BoolToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var paramStr = parameter?.ToString() ?? "True|False";
        var parts = paramStr.Split('|');
        
        if (parts.Length != 2)
            return value?.ToString() ?? "";
            
        bool boolValue = value is bool b && b;
        return boolValue ? parts[0] : parts[1];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter que devuelve un ancho de borde si el valor es igual al parámetro
/// Útil para selección de opciones
/// </summary>
public class EqualityToBorderWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return 0;

        // Intentar comparar como enteros
        if (int.TryParse(value.ToString(), out int intValue) &&
            int.TryParse(parameter.ToString(), out int intParam))
        {
            return intValue == intParam ? 2 : 0;
        }

        return value.Equals(parameter) ? 2 : 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter que devuelve true si la cadena no es null ni está vacía
/// </summary>
public class NotNullOrEmptyBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
            return !string.IsNullOrWhiteSpace(str);
        return value != null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
