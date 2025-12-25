#if WINDOWS
using Microsoft.UI.Xaml;
using Windows.Graphics.Display;
using WinRT.Interop;
using System.Runtime.InteropServices;
using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader.Platforms.Windows;

/// <summary>
/// Implementación de IUIScalingService para Windows.
/// Detecta la resolución, DPI y tamaño de pantalla para ajustar la UI proporcionalmente.
/// La referencia base es una pantalla de 1920x1080 a 96 DPI (100% de escala).
/// </summary>
public class WindowsUIScalingService : IUIScalingService
{
    // Resolución de referencia (pantalla para la que se diseñó la UI)
    private const double ReferenceWidth = 1920.0;
    private const double ReferenceHeight = 1080.0;
    private const double ReferenceDpi = 96.0;

    // Factor de reducción base para Windows (la UI original es demasiado grande)
    // 0.85 = 15% más pequeño que el diseño original
    private const double WindowsBaseReductionFactor = 0.85;

    // Límites de escala para evitar extremos
    private const double MinScaleFactor = 0.6;
    private const double MaxScaleFactor = 1.8;

    // Límites de escala de fuente (más conservadores)
    private const double MinFontScaleFactor = 0.75;
    private const double MaxFontScaleFactor = 1.3;

    private double _scaleFactor = 1.0;
    private double _fontScaleFactor = 1.0;
    private double _screenWidth;
    private double _screenHeight;
    private double _systemDpi = 96.0;
    private ScreenSizeCategory _sizeCategory = ScreenSizeCategory.Medium;

    public event EventHandler? ScaleChanged;

    public double ScaleFactor => _scaleFactor;
    public double ScreenWidth => _screenWidth;
    public double ScreenHeight => _screenHeight;
    public double SystemDpi => _systemDpi;
    public ScreenSizeCategory SizeCategory => _sizeCategory;

    public WindowsUIScalingService()
    {
        UpdateScale();
    }

    public void UpdateScale()
    {
        try
        {
            // Obtener información de la pantalla principal
            var displayInfo = GetDisplayInfo();
            _screenWidth = displayInfo.Width;
            _screenHeight = displayInfo.Height;
            _systemDpi = displayInfo.Dpi;

            // Calcular factor de escala basado en el área efectiva disponible
            // considerando tanto la resolución como el DPI del sistema
            double dpiScale = _systemDpi / ReferenceDpi;
            
            // Píxeles efectivos (después de aplicar el escalado del sistema)
            double effectiveWidth = _screenWidth / dpiScale;
            double effectiveHeight = _screenHeight / dpiScale;

            // Factor de escala basado en el espacio efectivo vs referencia
            double widthRatio = effectiveWidth / ReferenceWidth;
            double heightRatio = effectiveHeight / ReferenceHeight;

            // Usar el menor de los dos para asegurar que todo quepa
            double baseScale = Math.Min(widthRatio, heightRatio);

            // Aplicar factor de reducción base para Windows
            baseScale *= WindowsBaseReductionFactor;

            // Para pantallas de alta densidad (4K, etc.), ajustar el escalado
            // Si el DPI es mayor que la referencia pero la resolución nativa es alta,
            // podemos ser menos agresivos con la reducción
            if (_screenWidth >= 2560 && dpiScale > 1.0)
            {
                // Pantallas 4K/QHD con alto DPI - compensar parcialmente
                baseScale *= Math.Min(1.0 + (dpiScale - 1.0) * 0.25, 1.2);
            }

            // Aplicar límites
            _scaleFactor = Math.Clamp(baseScale, MinScaleFactor, MaxScaleFactor);

            // Factor de escala de fuente (proporcional pero ligeramente más pequeño)
            // Reducimos un poco más las fuentes para mayor densidad de información
            _fontScaleFactor = Math.Clamp(
                _scaleFactor * 0.95, // Fuentes 5% más pequeñas que el escalado general
                MinFontScaleFactor, 
                MaxFontScaleFactor
            );

            // Determinar categoría de pantalla
            _sizeCategory = DetermineSizeCategory(effectiveWidth, effectiveHeight);

            System.Diagnostics.Debug.WriteLine($"[UIScaling] Screen: {_screenWidth}x{_screenHeight} @ {_systemDpi} DPI");
            System.Diagnostics.Debug.WriteLine($"[UIScaling] Effective: {effectiveWidth:F0}x{effectiveHeight:F0}");
            System.Diagnostics.Debug.WriteLine($"[UIScaling] Scale Factor: {_scaleFactor:F2}, Font Scale: {_fontScaleFactor:F2}");
            System.Diagnostics.Debug.WriteLine($"[UIScaling] Category: {_sizeCategory}");

            ScaleChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UIScaling] Error updating scale: {ex.Message}");
            // Valores por defecto seguros
            _scaleFactor = 1.0;
            _fontScaleFactor = 1.0;
            _sizeCategory = ScreenSizeCategory.Medium;
        }
    }

    public double Scale(double value)
    {
        return value * _scaleFactor;
    }

    public double ScaleFont(double baseFontSize)
    {
        // Usar el factor de escala de fuente específico
        return Math.Round(baseFontSize * _fontScaleFactor);
    }

    public Microsoft.Maui.Thickness ScaleThickness(Microsoft.Maui.Thickness baseThickness)
    {
        return new Microsoft.Maui.Thickness(
            Scale(baseThickness.Left),
            Scale(baseThickness.Top),
            Scale(baseThickness.Right),
            Scale(baseThickness.Bottom)
        );
    }

    private ScreenSizeCategory DetermineSizeCategory(double effectiveWidth, double effectiveHeight)
    {
        double effectiveArea = effectiveWidth * effectiveHeight;

        if (effectiveArea < 1366 * 768)
            return ScreenSizeCategory.Small;
        if (effectiveArea < 1920 * 1080)
            return ScreenSizeCategory.Medium;
        if (effectiveArea < 2560 * 1440)
            return ScreenSizeCategory.Large;
        return ScreenSizeCategory.ExtraLarge;
    }

    private (double Width, double Height, double Dpi) GetDisplayInfo()
    {
        try
        {
            // Usar P/Invoke para obtener información precisa de la pantalla
            var hdc = GetDC(IntPtr.Zero);
            try
            {
                int dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
                int screenWidth = GetSystemMetrics(SM_CXSCREEN);
                int screenHeight = GetSystemMetrics(SM_CYSCREEN);

                return (screenWidth, screenHeight, dpiX);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }
        catch
        {
            // Fallback a valores por defecto
            return (1920, 1080, 96);
        }
    }

    #region P/Invoke

    private const int LOGPIXELSX = 88;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    #endregion
}
#endif
