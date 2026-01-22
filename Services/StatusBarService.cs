using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Maui.Graphics;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Entrada de log de la base de datos
/// </summary>
public class DatabaseLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Message { get; set; } = string.Empty;
    public DatabaseLogLevel Level { get; set; }
    
    public string TimestampText => Timestamp.ToString("HH:mm:ss");

    // NO usar emojis: el UI debe renderizar SF Symbols (o equivalente en Windows)
    public string LevelText => Level switch
    {
        DatabaseLogLevel.Info => "INFO",
        DatabaseLogLevel.Success => "OK",
        DatabaseLogLevel.Warning => "WARN",
        DatabaseLogLevel.Error => "ERROR",
        _ => "LOG"
    };

    public string LevelSymbolName => Level switch
    {
        DatabaseLogLevel.Info => "info.circle",
        DatabaseLogLevel.Success => "checkmark.circle.fill",
        DatabaseLogLevel.Warning => "exclamationmark.triangle.fill",
        DatabaseLogLevel.Error => "xmark.circle.fill",
        _ => "circle.fill"
    };

    public Color LevelTintColor => Level switch
    {
        DatabaseLogLevel.Info => Color.FromArgb("#FF6DDDFF"),
        DatabaseLogLevel.Success => Color.FromArgb("#FF4CAF50"),
        DatabaseLogLevel.Warning => Color.FromArgb("#FFFF9800"),
        DatabaseLogLevel.Error => Color.FromArgb("#FFFF5252"),
        _ => Color.FromArgb("#FF6A6A6A")
    };
}

public enum DatabaseLogLevel
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// Servicio global para gestionar el estado del footer/status bar de la aplicación
/// </summary>
public class StatusBarService : INotifyPropertyChanged
{
    private const int MaxLogEntries = 100;
    
    private string? _currentOperation;
    private string? _userName;
    private string? _userPhotoPath;
    private int _videoCount;
    private int _sessionCount;
    private bool _isOperationInProgress;
    private double _operationProgress; // 0.0 - 1.0
    private string? _lastSyncTime;
    private bool _isDatabaseOk = true;
    private string? _lastDatabaseError;
    private DateTime _lastDatabaseActivity;
    private bool _isBackendAvailable;
    private bool _isBackendChecking;
    private string? _lastBackendError;
    private DateTime _lastBackendCheck;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? StatusChanged;
    public event EventHandler? DatabaseLogAdded;

    /// <summary>
    /// Logs de la base de datos
    /// </summary>
    public ObservableCollection<DatabaseLogEntry> DatabaseLogs { get; } = new();

    /// <summary>
    /// Indica si la base de datos está funcionando correctamente
    /// </summary>
    public bool IsDatabaseOk
    {
        get => _isDatabaseOk;
        private set
        {
            if (_isDatabaseOk != value)
            {
                _isDatabaseOk = value;
                OnPropertyChanged(nameof(IsDatabaseOk));
                OnPropertyChanged(nameof(DatabaseStatusSymbol));
            }
        }
    }

    /// <summary>
    /// Último error de la base de datos
    /// </summary>
    public string? LastDatabaseError
    {
        get => _lastDatabaseError;
        private set
        {
            if (_lastDatabaseError != value)
            {
                _lastDatabaseError = value;
                OnPropertyChanged(nameof(LastDatabaseError));
            }
        }
    }

    /// <summary>
    /// Símbolo SF para el estado de la BD
    /// </summary>
    public string DatabaseStatusSymbol => _isDatabaseOk ? "checkmark.circle.fill" : "exclamationmark.triangle.fill";

    /// <summary>
    /// Última actividad de la base de datos
    /// </summary>
    public DateTime LastDatabaseActivity
    {
        get => _lastDatabaseActivity;
        private set
        {
            if (_lastDatabaseActivity != value)
            {
                _lastDatabaseActivity = value;
                OnPropertyChanged(nameof(LastDatabaseActivity));
                OnPropertyChanged(nameof(LastDatabaseActivityText));
            }
        }
    }

    public string LastDatabaseActivityText => _lastDatabaseActivity == default 
        ? "Sin actividad" 
        : $"Última: {_lastDatabaseActivity:HH:mm:ss}";

    /// <summary>
    /// Indica si el backend está disponible
    /// </summary>
    public bool IsBackendAvailable
    {
        get => _isBackendAvailable;
        private set
        {
            if (_isBackendAvailable != value)
            {
                _isBackendAvailable = value;
                OnPropertyChanged(nameof(IsBackendAvailable));
                OnPropertyChanged(nameof(BackendStatusText));
                OnPropertyChanged(nameof(BackendStatusSymbol));
                OnPropertyChanged(nameof(BackendStatusColor));
            }
        }
    }

    /// <summary>
    /// Indica si se está comprobando el backend
    /// </summary>
    public bool IsBackendChecking
    {
        get => _isBackendChecking;
        private set
        {
            if (_isBackendChecking != value)
            {
                _isBackendChecking = value;
                OnPropertyChanged(nameof(IsBackendChecking));
                OnPropertyChanged(nameof(BackendStatusText));
                OnPropertyChanged(nameof(BackendStatusSymbol));
                OnPropertyChanged(nameof(BackendStatusColor));
            }
        }
    }

    /// <summary>
    /// Último error del backend
    /// </summary>
    public string? LastBackendError
    {
        get => _lastBackendError;
        private set
        {
            if (_lastBackendError != value)
            {
                _lastBackendError = value;
                OnPropertyChanged(nameof(LastBackendError));
            }
        }
    }

    /// <summary>
    /// Última comprobación del backend
    /// </summary>
    public DateTime LastBackendCheck
    {
        get => _lastBackendCheck;
        private set
        {
            if (_lastBackendCheck != value)
            {
                _lastBackendCheck = value;
                OnPropertyChanged(nameof(LastBackendCheck));
            }
        }
    }

    public string BackendStatusText
    {
        get
        {
            if (IsBackendChecking)
                return "Backend...";
            return IsBackendAvailable ? "Backend OK" : "Backend OFF";
        }
    }

    public string BackendStatusSymbol
    {
        get
        {
            if (IsBackendChecking)
                return "arrow.triangle.2.circlepath";
            return IsBackendAvailable ? "checkmark.circle.fill" : "exclamationmark.triangle.fill";
        }
    }

    public Color BackendStatusColor
    {
        get
        {
            if (IsBackendChecking)
                return Color.FromArgb("#FF6DDDFF");
            return IsBackendAvailable ? Color.FromArgb("#FF4CAF50") : Color.FromArgb("#FFFF9800");
        }
    }

    /// <summary>
    /// Operación actual en curso (ej: "Importando sesión...", "Exportando vídeos...")
    /// </summary>
    public string? CurrentOperation
    {
        get => _currentOperation;
        set
        {
            if (_currentOperation != value)
            {
                _currentOperation = value;
                OnPropertyChanged(nameof(CurrentOperation));
                OnPropertyChanged(nameof(HasCurrentOperation));
            }
        }
    }

    public bool HasCurrentOperation => !string.IsNullOrWhiteSpace(_currentOperation);

    /// <summary>
    /// Indica si hay una operación en progreso
    /// </summary>
    public bool IsOperationInProgress
    {
        get => _isOperationInProgress;
        set
        {
            if (_isOperationInProgress != value)
            {
                _isOperationInProgress = value;
                OnPropertyChanged(nameof(IsOperationInProgress));
            }
        }
    }

    /// <summary>
    /// Progreso de la operación actual (0.0 a 1.0)
    /// </summary>
    public double OperationProgress
    {
        get => _operationProgress;
        set
        {
            if (Math.Abs(_operationProgress - value) > 0.001)
            {
                _operationProgress = Math.Clamp(value, 0.0, 1.0);
                OnPropertyChanged(nameof(OperationProgress));
                OnPropertyChanged(nameof(OperationProgressPercent));
            }
        }
    }

    public int OperationProgressPercent => (int)(_operationProgress * 100);

    /// <summary>
    /// Actualiza el estado del backend para el footer
    /// </summary>
    public void UpdateBackendStatus(bool isAvailable, string? errorMessage = null, bool isChecking = false)
    {
        IsBackendChecking = isChecking;
        IsBackendAvailable = isAvailable;
        LastBackendError = errorMessage;
        LastBackendCheck = DateTime.UtcNow;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Nombre del usuario actual
    /// </summary>
    public string? UserName
    {
        get => _userName;
        set
        {
            if (_userName != value)
            {
                _userName = value;
                OnPropertyChanged(nameof(UserName));
                OnPropertyChanged(nameof(UserDisplayName));
            }
        }
    }

    public string UserDisplayName => string.IsNullOrWhiteSpace(_userName) ? "Sin perfil" : _userName;

    /// <summary>
    /// Ruta de la foto del usuario
    /// </summary>
    public string? UserPhotoPath
    {
        get => _userPhotoPath;
        set
        {
            if (_userPhotoPath != value)
            {
                _userPhotoPath = value;
                OnPropertyChanged(nameof(UserPhotoPath));
                OnPropertyChanged(nameof(HasUserPhoto));
            }
        }
    }

    public bool HasUserPhoto => !string.IsNullOrWhiteSpace(_userPhotoPath);

    /// <summary>
    /// Número total de vídeos en la base de datos
    /// </summary>
    public int VideoCount
    {
        get => _videoCount;
        set
        {
            if (_videoCount != value)
            {
                _videoCount = value;
                OnPropertyChanged(nameof(VideoCount));
                OnPropertyChanged(nameof(VideoCountText));
            }
        }
    }

    public string VideoCountText => $"{_videoCount} vídeos";

    /// <summary>
    /// Número total de sesiones en la base de datos
    /// </summary>
    public int SessionCount
    {
        get => _sessionCount;
        set
        {
            if (_sessionCount != value)
            {
                _sessionCount = value;
                OnPropertyChanged(nameof(SessionCount));
                OnPropertyChanged(nameof(SessionCountText));
            }
        }
    }

    public string SessionCountText => $"{_sessionCount} sesiones";

    /// <summary>
    /// Última sincronización o actualización
    /// </summary>
    public string? LastSyncTime
    {
        get => _lastSyncTime;
        set
        {
            if (_lastSyncTime != value)
            {
                _lastSyncTime = value;
                OnPropertyChanged(nameof(LastSyncTime));
            }
        }
    }

    /// <summary>
    /// Inicia una operación con mensaje
    /// </summary>
    public void StartOperation(string operationName)
    {
        CurrentOperation = operationName;
        IsOperationInProgress = true;
        OperationProgress = 0;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Actualiza el progreso de la operación actual
    /// </summary>
    public void UpdateProgress(double progress, string? message = null)
    {
        OperationProgress = progress;
        if (message != null)
            CurrentOperation = message;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Finaliza la operación actual
    /// </summary>
    public void EndOperation(string? completionMessage = null)
    {
        if (completionMessage != null)
        {
            CurrentOperation = completionMessage;
            // Mostrar mensaje de completado brevemente y luego limpiar
            _ = ClearOperationAfterDelayAsync();
        }
        else
        {
            CurrentOperation = null;
        }
        IsOperationInProgress = false;
        OperationProgress = 0;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ClearOperationAfterDelayAsync()
    {
        await Task.Delay(3000);
        CurrentOperation = null;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Actualiza la información del usuario
    /// </summary>
    public void UpdateUserInfo(string? fullName, string? photoPath)
    {
        UserName = fullName;
        UserPhotoPath = photoPath;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Actualiza los contadores de la base de datos
    /// </summary>
    public void UpdateCounts(int videos, int sessions)
    {
        VideoCount = videos;
        SessionCount = sessions;
        LastSyncTime = DateTime.Now.ToString("HH:mm");
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Registra una operación de base de datos exitosa
    /// </summary>
    public void LogDatabaseSuccess(string message)
    {
        AddDatabaseLog(message, DatabaseLogLevel.Success);
        IsDatabaseOk = true;
        LastDatabaseError = null;
    }

    /// <summary>
    /// Registra información de la base de datos
    /// </summary>
    public void LogDatabaseInfo(string message)
    {
        AddDatabaseLog(message, DatabaseLogLevel.Info);
        LastDatabaseActivity = DateTime.Now;
    }

    /// <summary>
    /// Registra una advertencia de la base de datos
    /// </summary>
    public void LogDatabaseWarning(string message)
    {
        AddDatabaseLog(message, DatabaseLogLevel.Warning);
    }

    /// <summary>
    /// Registra un error de la base de datos
    /// </summary>
    public void LogDatabaseError(string message)
    {
        AddDatabaseLog(message, DatabaseLogLevel.Error);
        IsDatabaseOk = false;
        LastDatabaseError = message;
    }

    /// <summary>
    /// Marca la BD como OK después de una recuperación
    /// </summary>
    public void MarkDatabaseOk()
    {
        IsDatabaseOk = true;
        LastDatabaseError = null;
        LogDatabaseSuccess("Base de datos operativa");
    }

    private void AddDatabaseLog(string message, DatabaseLogLevel level)
    {
        var entry = new DatabaseLogEntry
        {
            Timestamp = DateTime.Now,
            Message = message,
            Level = level
        };

        // Añadir al principio para mostrar los más recientes primero
        MainThread.BeginInvokeOnMainThread(() =>
        {
            DatabaseLogs.Insert(0, entry);
            
            // Limitar el número de entradas
            while (DatabaseLogs.Count > MaxLogEntries)
            {
                DatabaseLogs.RemoveAt(DatabaseLogs.Count - 1);
            }
            
            DatabaseLogAdded?.Invoke(this, EventArgs.Empty);
        });

        LastDatabaseActivity = DateTime.Now;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Limpia todos los logs de la base de datos
    /// </summary>
    public void ClearDatabaseLogs()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            DatabaseLogs.Clear();
        });
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
