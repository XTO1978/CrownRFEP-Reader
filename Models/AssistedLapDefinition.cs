using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Define un parcial prefijado para el modo de toma asistida.
/// El usuario configura el nombre (max 8 caracteres) antes de iniciar la toma.
/// </summary>
public class AssistedLapDefinition : INotifyPropertyChanged
{
    private int _index;
    private string _name = "";
    private AssistedLapDefinition? _previousLap;
    private long? _markedMs;
    private bool _isMarked;
    private bool _isCurrent;
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    /// <summary>Índice del parcial (1, 2, 3...)</summary>
    public int Index
    {
        get => _index;
        set => SetProperty(ref _index, value);
    }
    
    /// <summary>Nombre del parcial (máximo 8 caracteres)</summary>
    public string Name
    {
        get => _name;
        set
        {
            // Limitar a 8 caracteres
            var truncated = string.IsNullOrEmpty(value) ? "" : (value.Length > 8 ? value[..8] : value);
            if (SetProperty(ref _name, truncated))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }
    
    /// <summary>Indica si este es el primer parcial del conjunto</summary>
    public bool IsFirstLap { get; set; }
    
    /// <summary>Indica si este es el último parcial del conjunto</summary>
    public bool IsLastLap { get; set; }

    /// <summary>
    /// Referencia al parcial anterior. Se usa para calcular prefijos y DisplayName.
    /// </summary>
    public AssistedLapDefinition? PreviousLap
    {
        get => _previousLap;
        set
        {
            if (ReferenceEquals(_previousLap, value)) return;

            if (_previousLap != null)
            {
                _previousLap.PropertyChanged -= PreviousLapOnPropertyChanged;
            }

            _previousLap = value;

            if (_previousLap != null)
            {
                _previousLap.PropertyChanged += PreviousLapOnPropertyChanged;
            }

            OnPropertyChanged(nameof(PreviousPointLabel));
            OnPropertyChanged(nameof(DisplayName));
        }
    }
    
    /// <summary>Nombre del parcial anterior (para mostrar el rango)</summary>
    private string _previousLapName = "";
    public string PreviousLapName 
    { 
        get => _previousLapName;
        set
        {
            if (SetProperty(ref _previousLapName, value ?? ""))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(PreviousPointLabel));
            }
        }
    }

    /// <summary>
    /// Texto para el label a la izquierda del Entry (punto anterior).
    /// Primer parcial: "Inicio"; siguientes: "{NombreAnterior}-".
    /// </summary>
    public string PreviousPointLabel
    {
        get
        {
            if (IsFirstLap || Index == 1)
            {
                return "Inicio";
            }

            var prevName = _previousLap != null
                ? (string.IsNullOrWhiteSpace(_previousLap.Name) ? $"P{_previousLap.Index}" : _previousLap.Name)
                : (string.IsNullOrWhiteSpace(PreviousLapName) ? $"P{Index - 1}" : PreviousLapName);

            return $"{prevName}-";
        }
    }
    
    /// <summary>Nombre para mostrar (rango desde el punto anterior hasta este)</summary>
    public string DisplayName
    {
        get
        {
            var currentName = string.IsNullOrWhiteSpace(Name) ? $"P{Index}" : Name;
            
            // Para el primer parcial, siempre usar "Ini"
            // Para los demás, usar el nombre del parcial anterior
            string prevName;
            if (IsFirstLap || Index == 1)
            {
                prevName = "Inicio";
            }
            else
            {
                prevName = _previousLap != null
                    ? (string.IsNullOrWhiteSpace(_previousLap.Name) ? $"P{_previousLap.Index}" : _previousLap.Name)
                    : (string.IsNullOrWhiteSpace(PreviousLapName) ? $"P{Index - 1}" : PreviousLapName);
            }
            
            // Mostrar como "PuntoAnterior-PuntoActual"
            return $"{prevName}-{currentName}";
        }
    }

    private void PreviousLapOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Name))
        {
            OnPropertyChanged(nameof(PreviousPointLabel));
            OnPropertyChanged(nameof(DisplayName));
        }
    }
    
    /// <summary>Timestamp marcado (en milisegundos desde inicio del video)</summary>
    public long? MarkedMs
    {
        get => _markedMs;
        set
        {
            if (SetProperty(ref _markedMs, value))
            {
                IsMarked = value.HasValue;
                OnPropertyChanged(nameof(MarkedTimeFormatted));
            }
        }
    }
    
    /// <summary>Indica si este parcial ya fue marcado</summary>
    public bool IsMarked
    {
        get => _isMarked;
        private set => SetProperty(ref _isMarked, value);
    }
    
    /// <summary>Indica si este es el parcial actual a marcar</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }
    
    /// <summary>Tiempo marcado formateado</summary>
    public string MarkedTimeFormatted
    {
        get
        {
            if (!MarkedMs.HasValue) return "--:--.---";
            var ts = TimeSpan.FromMilliseconds(MarkedMs.Value);
            return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
        }
    }
    
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
    
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    
    /// <summary>Método público para forzar notificación de cambio de propiedad</summary>
    public void OnPropertyChangedPublic(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Estados del flujo de toma asistida
/// </summary>
public enum AssistedLapState
{
    /// <summary>Configurando parciales (antes de iniciar)</summary>
    Configuring,
    
    /// <summary>Esperando marcar el punto de inicio</summary>
    WaitingForStart,
    
    /// <summary>Marcando parciales uno a uno</summary>
    MarkingLaps,
    
    /// <summary>Esperando marcar el punto de fin</summary>
    WaitingForEnd,
    
    /// <summary>Toma completada</summary>
    Completed
}
