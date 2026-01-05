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
            SetProperty(ref _name, truncated);
        }
    }
    
    /// <summary>Nombre para mostrar (usa el índice si no hay nombre)</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"P{Index}" : Name;
    
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
