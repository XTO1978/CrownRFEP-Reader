namespace CrownRFEP_Reader.Services;

/// <summary>
/// Representa el estado de una tarea de importación
/// </summary>
public class ImportTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int Percentage => TotalFiles > 0 ? (ProcessedFiles * 100) / TotalFiles : 0;
    public string CurrentFile { get; set; } = "";
    public bool IsCompleted { get; set; }
    public bool HasError { get; set; }
    public string? ErrorMessage { get; set; }
    public int? CreatedSessionId { get; set; }
}

/// <summary>
/// Servicio para gestionar importaciones en segundo plano.
/// Permite que las importaciones continúen incluso si el usuario sale de ImportPage.
/// </summary>
public class ImportProgressService : IDisposable
{
    private readonly object _lock = new();
    private ImportTask? _currentTask;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Evento disparado cuando el progreso de importación cambia
    /// </summary>
    public event EventHandler<ImportTask>? ProgressChanged;

    /// <summary>
    /// Evento disparado cuando la importación se completa (éxito o error)
    /// </summary>
    public event EventHandler<ImportTask>? ImportCompleted;

    /// <summary>
    /// Indica si hay una importación en curso
    /// </summary>
    public bool IsImporting => _currentTask != null && !_currentTask.IsCompleted;

    /// <summary>
    /// Obtiene la tarea de importación actual
    /// </summary>
    public ImportTask? CurrentTask => _currentTask;

    /// <summary>
    /// Inicia una nueva tarea de importación
    /// </summary>
    public ImportTask StartImport(string name, int totalFiles)
    {
        lock (_lock)
        {
            // Cancelar tarea anterior si existe
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            _currentTask = new ImportTask
            {
                Name = name,
                TotalFiles = totalFiles,
                ProcessedFiles = 0,
                IsCompleted = false
            };

            NotifyProgressChanged();
            return _currentTask;
        }
    }

    /// <summary>
    /// Actualiza el progreso de la importación actual
    /// </summary>
    public void UpdateProgress(int processedFiles, string currentFile)
    {
        lock (_lock)
        {
            if (_currentTask == null) return;

            _currentTask.ProcessedFiles = processedFiles;
            _currentTask.CurrentFile = currentFile;
            NotifyProgressChanged();
        }
    }

    /// <summary>
    /// Marca la importación como completada exitosamente
    /// </summary>
    public void CompleteImport(int? sessionId = null)
    {
        lock (_lock)
        {
            if (_currentTask == null) return;

            _currentTask.IsCompleted = true;
            _currentTask.ProcessedFiles = _currentTask.TotalFiles;
            _currentTask.CreatedSessionId = sessionId;
            
            NotifyProgressChanged();
            NotifyImportCompleted();
        }
    }

    /// <summary>
    /// Marca la importación como fallida
    /// </summary>
    public void FailImport(string errorMessage)
    {
        lock (_lock)
        {
            if (_currentTask == null) return;

            _currentTask.IsCompleted = true;
            _currentTask.HasError = true;
            _currentTask.ErrorMessage = errorMessage;
            
            NotifyProgressChanged();
            NotifyImportCompleted();
        }
    }

    /// <summary>
    /// Cancela la importación actual
    /// </summary>
    public void CancelImport()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            if (_currentTask != null)
            {
                _currentTask.IsCompleted = true;
                _currentTask.HasError = true;
                _currentTask.ErrorMessage = "Cancelado por el usuario";
                NotifyImportCompleted();
            }
        }
    }

    /// <summary>
    /// Limpia la tarea completada
    /// </summary>
    public void ClearCompletedTask()
    {
        lock (_lock)
        {
            if (_currentTask?.IsCompleted == true)
            {
                _currentTask = null;
                NotifyProgressChanged();
            }
        }
    }

    /// <summary>
    /// Obtiene el token de cancelación actual
    /// </summary>
    public CancellationToken GetCancellationToken()
    {
        return _cts?.Token ?? CancellationToken.None;
    }

    private void NotifyProgressChanged()
    {
        if (_currentTask != null)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ProgressChanged?.Invoke(this, _currentTask);
            });
        }
    }

    private void NotifyImportCompleted()
    {
        if (_currentTask != null)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ImportCompleted?.Invoke(this, _currentTask);
            });
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
