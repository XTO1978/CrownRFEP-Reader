using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa una sección o tramo del río durante la grabación
/// </summary>
public class RiverSection : INotifyPropertyChanged
{
    private bool _isSelected;
    
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// ID único de la sección
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Nombre de la sección (ej: "Salida", "Tramo 1", "Llegada")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Orden de la sección en el río
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Color para identificar la sección en la UI
    /// </summary>
    public string Color { get; set; } = "#6DDDFF";

    /// <summary>
    /// Indica si esta sección está seleccionada actualmente
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Representa una sesión de grabación activa con todos sus metadatos y eventos
/// </summary>
public class CameraRecordingSession : INotifyPropertyChanged
{
    private DateTime _startTime;
    private DateTime? _endTime;
    private bool _isRecording;
    private long _elapsedMilliseconds;
    private int? _currentAthleteId;
    private string? _currentAthleteName;
    private int? _currentSectionId;
    private string? _currentSectionName;
    private double _currentZoomFactor = 1.0;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// ID único de la sesión de grabación
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// ID de la sesión en la base de datos (si existe)
    /// Los videos grabados se asociarán a esta sesión
    /// </summary>
    public int? DatabaseSessionId { get; set; }

    /// <summary>
    /// Nombre de la sesión
    /// </summary>
    public string SessionName { get; set; } = string.Empty;

    /// <summary>
    /// Tipo de sesión (Entrenamiento, Competición, etc.)
    /// </summary>
    public string SessionType { get; set; } = "Entrenamiento";

    /// <summary>
    /// Lugar donde se realiza la sesión
    /// </summary>
    public string? Place { get; set; }

    /// <summary>
    /// ID del lugar en la base de datos
    /// </summary>
    public int? PlaceId { get; set; }

    /// <summary>
    /// Entrenador responsable
    /// </summary>
    public string? Coach { get; set; }

    /// <summary>
    /// Hora de inicio de la grabación
    /// </summary>
    public DateTime StartTime
    {
        get => _startTime;
        set
        {
            if (_startTime != value)
            {
                _startTime = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Hora de fin de la grabación
    /// </summary>
    public DateTime? EndTime
    {
        get => _endTime;
        set
        {
            if (_endTime != value)
            {
                _endTime = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Duration));
            }
        }
    }

    /// <summary>
    /// Indica si se está grabando actualmente
    /// </summary>
    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (_isRecording != value)
            {
                _isRecording = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Tiempo transcurrido desde el inicio (en milisegundos)
    /// </summary>
    public long ElapsedMilliseconds
    {
        get => _elapsedMilliseconds;
        set
        {
            if (_elapsedMilliseconds != value)
            {
                _elapsedMilliseconds = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedElapsedTime));
            }
        }
    }

    /// <summary>
    /// Duración total de la grabación
    /// </summary>
    public TimeSpan Duration => EndTime.HasValue 
        ? EndTime.Value - StartTime 
        : TimeSpan.FromMilliseconds(ElapsedMilliseconds);

    /// <summary>
    /// Tiempo transcurrido formateado (MM:SS.mmm)
    /// </summary>
    public string FormattedElapsedTime
    {
        get
        {
            var ts = TimeSpan.FromMilliseconds(ElapsedMilliseconds);
            return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}.{ts.Milliseconds / 10:00}";
        }
    }

    /// <summary>
    /// ID del deportista actualmente activo
    /// </summary>
    public int? CurrentAthleteId
    {
        get => _currentAthleteId;
        set
        {
            if (_currentAthleteId != value)
            {
                _currentAthleteId = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Nombre del deportista actualmente activo
    /// </summary>
    public string? CurrentAthleteName
    {
        get => _currentAthleteName;
        set
        {
            if (_currentAthleteName != value)
            {
                _currentAthleteName = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// ID de la sección actual
    /// </summary>
    public int? CurrentSectionId
    {
        get => _currentSectionId;
        set
        {
            if (_currentSectionId != value)
            {
                _currentSectionId = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Nombre de la sección actual
    /// </summary>
    public string? CurrentSectionName
    {
        get => _currentSectionName;
        set
        {
            if (_currentSectionName != value)
            {
                _currentSectionName = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Factor de zoom actual de la cámara (1.0 = sin zoom)
    /// </summary>
    public double CurrentZoomFactor
    {
        get => _currentZoomFactor;
        set
        {
            if (Math.Abs(_currentZoomFactor - value) > 0.001)
            {
                _currentZoomFactor = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Ruta del archivo de video grabado
    /// </summary>
    public string? VideoFilePath { get; set; }

    /// <summary>
    /// Ruta de la miniatura generada
    /// </summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>
    /// Lista de deportistas participantes en la sesión
    /// </summary>
    public ObservableCollection<Athlete> Participants { get; set; } = new();

    /// <summary>
    /// Lista de secciones/tramos disponibles
    /// </summary>
    public ObservableCollection<RiverSection> Sections { get; set; } = new();

    /// <summary>
    /// Lista de eventos registrados durante la grabación
    /// </summary>
    public ObservableCollection<RecordingEvent> Events { get; set; } = new();

    /// <summary>
    /// Lista de etiquetas disponibles para marcar eventos
    /// </summary>
    public ObservableCollection<Tag> AvailableTags { get; set; } = new();

    /// <summary>
    /// Número de laps registrados
    /// </summary>
    public int LapCount => Events.Count(e => e.EventType == RecordingEventType.Lap);

    /// <summary>
    /// Agrega un evento a la sesión
    /// </summary>
    public RecordingEvent AddEvent(RecordingEventType eventType, string? label = null)
    {
        // Desmarcar el evento anterior como "último"
        if (Events.Count > 0)
        {
            Events[Events.Count - 1].IsLatest = false;
        }

        var recordingEvent = new RecordingEvent
        {
            EventType = eventType,
            ElapsedMilliseconds = ElapsedMilliseconds,
            Timestamp = DateTime.Now,
            AthleteId = CurrentAthleteId,
            AthleteName = CurrentAthleteName,
            SectionId = CurrentSectionId,
            SectionName = CurrentSectionName,
            Label = label,
            IsLatest = true  // El nuevo evento es el último
        };

        Events.Add(recordingEvent);
        OnPropertyChanged(nameof(LapCount));
        return recordingEvent;
    }

    /// <summary>
    /// Agrega un evento de lap
    /// </summary>
    public RecordingEvent AddLap(string? label = null)
    {
        var lapNumber = LapCount + 1;
        return AddEvent(RecordingEventType.Lap, label ?? $"Lap {lapNumber}");
    }

    /// <summary>
    /// Agrega un evento de etiqueta
    /// </summary>
    public RecordingEvent AddTag(Tag tag)
    {
        var recordingEvent = AddEvent(RecordingEventType.Tag);
        recordingEvent.TagId = tag.Id;
        recordingEvent.TagName = tag.NombreTag;
        return recordingEvent;
    }

    /// <summary>
    /// Cambia el deportista activo
    /// </summary>
    public RecordingEvent ChangeAthlete(Athlete athlete)
    {
        CurrentAthleteId = athlete.Id;
        CurrentAthleteName = athlete.NombreCompleto;
        return AddEvent(RecordingEventType.AthleteChange);
    }

    /// <summary>
    /// Cambia la sección actual
    /// </summary>
    public RecordingEvent ChangeSection(RiverSection section)
    {
        CurrentSectionId = section.Id;
        CurrentSectionName = section.Name;
        return AddEvent(RecordingEventType.SectionChange);
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
