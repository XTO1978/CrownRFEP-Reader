using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa un evento de etiqueta asociado a un momento específico del video.
/// Agrupa la información de Input con el Tag correspondiente.
/// </summary>
public class TagEvent : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Id del Input en la base de datos
    /// </summary>
    public int InputId { get; set; }

    /// <summary>
    /// Id del Tag
    /// </summary>
    public int TagId { get; set; }

    /// <summary>
    /// Nombre del tag/etiqueta
    /// </summary>
    public string TagName { get; set; } = "";

    /// <summary>
    /// Posición del video en milisegundos donde se marcó el evento
    /// </summary>
    public long TimestampMs { get; set; }

    /// <summary>
    /// Posición formateada como mm:ss.f
    /// </summary>
    public string TimestampFormatted
    {
        get
        {
            var ts = TimeSpan.FromMilliseconds(TimestampMs);
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 100}";
            return $"{ts.Minutes}:{ts.Seconds:D2}.{ts.Milliseconds / 100}";
        }
    }

    /// <summary>
    /// Color de fondo del evento (verde por defecto)
    /// </summary>
    public string BackgroundColor { get; set; } = "#FF66BB6A";

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
