using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Tipos de eventos que se pueden registrar durante la grabación
/// </summary>
public enum RecordingEventType
{
    /// <summary>Inicio de la grabación</summary>
    Start,
    /// <summary>Fin de la grabación</summary>
    Stop,
    /// <summary>Lap intermedio (tiempo parcial)</summary>
    Lap,
    /// <summary>Inicio de ejecución del deportista</summary>
    ExecutionStart,
    /// <summary>Fin de ejecución del deportista</summary>
    ExecutionEnd,
    /// <summary>Etiqueta de evento personalizada</summary>
    Tag,
    /// <summary>Cambio de deportista activo</summary>
    AthleteChange,
    /// <summary>Cambio de sección/tramo</summary>
    SectionChange
}

/// <summary>
/// Representa un evento durante la grabación de una sesión
/// </summary>
public class RecordingEvent : INotifyPropertyChanged
{
    private string? _label;
    private bool _isLatest;
    
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// ID único del evento
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Indica si este es el último evento añadido
    /// </summary>
    public bool IsLatest
    {
        get => _isLatest;
        set
        {
            if (_isLatest != value)
            {
                _isLatest = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BackgroundColor));
            }
        }
    }

    /// <summary>
    /// Color de fondo del evento (destacado si es el último)
    /// </summary>
    public string BackgroundColor => IsLatest ? "#30FFFFFF" : "Transparent";

    /// <summary>
    /// Tipo de evento
    /// </summary>
    public RecordingEventType EventType { get; set; }

    /// <summary>
    /// Tiempo desde el inicio de la grabación (en milisegundos)
    /// </summary>
    public long ElapsedMilliseconds { get; set; }

    /// <summary>
    /// Timestamp absoluto del evento
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// ID del deportista asociado (si aplica)
    /// </summary>
    public int? AthleteId { get; set; }

    /// <summary>
    /// Nombre del deportista (para mostrar sin necesidad de query)
    /// </summary>
    public string? AthleteName { get; set; }

    /// <summary>
    /// ID de la sección/tramo (si aplica)
    /// </summary>
    public int? SectionId { get; set; }

    /// <summary>
    /// Nombre de la sección (para mostrar)
    /// </summary>
    public string? SectionName { get; set; }

    /// <summary>
    /// ID del tag asociado (si aplica)
    /// </summary>
    public int? TagId { get; set; }

    /// <summary>
    /// Nombre del tag (para mostrar)
    /// </summary>
    public string? TagName { get; set; }

    /// <summary>
    /// Etiqueta o nota adicional del evento
    /// </summary>
    public string? Label
    {
        get => _label;
        set
        {
            if (_label != value)
            {
                _label = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Tiempo formateado para mostrar (MM:SS.mmm)
    /// </summary>
    public string FormattedTime
    {
        get
        {
            var ts = TimeSpan.FromMilliseconds(ElapsedMilliseconds);
            return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
        }
    }

    /// <summary>
    /// Descripción completa del evento para mostrar en la UI
    /// </summary>
    public string DisplayDescription
    {
        get
        {
            return EventType switch
            {
                RecordingEventType.Start => "Inicio",
                RecordingEventType.Stop => "Fin",
                RecordingEventType.Lap => $"Lap {Label ?? ""}",
                RecordingEventType.ExecutionStart => $"Inicio: {AthleteName ?? "Sin deportista"}",
                RecordingEventType.ExecutionEnd => $"Fin: {AthleteName ?? "Sin deportista"}",
                RecordingEventType.Tag => $"{TagName ?? Label ?? "Etiqueta"}",
                RecordingEventType.AthleteChange => $"{AthleteName ?? "Sin deportista"}",
                RecordingEventType.SectionChange => $"{SectionName ?? "Sin sección"}",
                _ => Label ?? EventType.ToString()
            };
        }
    }

    /// <summary>
    /// Nombre del SF Symbol para el tipo de evento
    /// </summary>
    public string SymbolName
    {
        get
        {
            return EventType switch
            {
                RecordingEventType.Start => "play.fill",
                RecordingEventType.Stop => "stop.fill",
                RecordingEventType.Lap => "flag.fill",
                RecordingEventType.ExecutionStart => "play.circle.fill",
                RecordingEventType.ExecutionEnd => "stop.circle.fill",
                RecordingEventType.Tag => "tag.fill",
                RecordingEventType.AthleteChange => "person.fill",
                RecordingEventType.SectionChange => "mappin.and.ellipse",
                _ => "circle.fill"
            };
        }
    }

    /// <summary>
    /// Color del símbolo según el tipo de evento
    /// </summary>
    public string SymbolColor
    {
        get
        {
            return EventType switch
            {
                RecordingEventType.Start => "#4CAF50",        // Verde
                RecordingEventType.Stop => "#F44336",         // Rojo
                RecordingEventType.Lap => "#2196F3",          // Azul
                RecordingEventType.ExecutionStart => "#4CAF50", // Verde
                RecordingEventType.ExecutionEnd => "#F44336",   // Rojo
                RecordingEventType.Tag => "#9C27B0",          // Púrpura
                RecordingEventType.AthleteChange => "#FF9800", // Naranja
                RecordingEventType.SectionChange => "#00BCD4", // Cyan
                _ => "#FFFFFF"
            };
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
