using CrownRFEP_Reader.Controls;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Coordina la reproducción sincronizada de múltiples PrecisionVideoPlayer
/// para garantizar precisión de frame en modo comparación.
/// </summary>
public class SyncPlaybackCoordinator
{
    private readonly List<PrecisionVideoPlayer> _players = new();
    private bool _isPlaying;
    private double _speed = 1.0;

    /// <summary>
    /// Indica si la reproducción sincronizada está activa
    /// </summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>
    /// Velocidad de reproducción actual
    /// </summary>
    public double Speed
    {
        get => _speed;
        set
        {
            _speed = value;
            foreach (var player in _players)
            {
                player.Speed = value;
            }
        }
    }

    /// <summary>
    /// Registra un player para sincronización
    /// </summary>
    public void RegisterPlayer(PrecisionVideoPlayer player)
    {
        if (!_players.Contains(player))
        {
            _players.Add(player);
        }
    }

    /// <summary>
    /// Elimina un player de la sincronización
    /// </summary>
    public void UnregisterPlayer(PrecisionVideoPlayer player)
    {
        _players.Remove(player);
    }

    /// <summary>
    /// Elimina todos los players excepto el principal
    /// </summary>
    public void ClearSecondaryPlayers()
    {
        if (_players.Count > 1)
        {
            _players.RemoveRange(1, _players.Count - 1);
        }
    }

    /// <summary>
    /// Elimina todos los players
    /// </summary>
    public void ClearAllPlayers()
    {
        _players.Clear();
    }

    /// <summary>
    /// Inicia reproducción sincronizada de todos los players.
    /// Usa SyncPlayRequested para coordinación precisa a nivel nativo.
    /// </summary>
    public void PlayAll(TimeSpan startPosition)
    {
        if (_players.Count == 0) return;

        _isPlaying = true;

        // Primero, hacer seek a la posición de inicio en todos los players
        foreach (var player in _players)
        {
            player.SeekTo(startPosition);
        }

        // Luego disparar el evento de play sincronizado
        // El handler nativo usará setRate:time:atHostTime: para sincronizar
        SyncPlayRequested?.Invoke(this, new SyncPlayEventArgs(
            _players.ToArray(),
            startPosition,
            _speed
        ));
    }

    /// <summary>
    /// Pausa todos los players simultáneamente
    /// </summary>
    public void PauseAll()
    {
        _isPlaying = false;

        // Disparar evento de pausa sincronizada
        SyncPauseRequested?.Invoke(this, _players.ToArray());
    }

    /// <summary>
    /// Hace seek sincronizado en todos los players
    /// </summary>
    public void SeekAll(TimeSpan position)
    {
        SyncSeekRequested?.Invoke(this, new SyncSeekEventArgs(
            _players.ToArray(),
            position
        ));
    }

    /// <summary>
    /// Avanza un frame en todos los players
    /// </summary>
    public void StepForwardAll()
    {
        foreach (var player in _players)
        {
            player.StepForward();
        }
    }

    /// <summary>
    /// Retrocede un frame en todos los players
    /// </summary>
    public void StepBackwardAll()
    {
        foreach (var player in _players)
        {
            player.StepBackward();
        }
    }

    /// <summary>
    /// Detiene todos los players
    /// </summary>
    public void StopAll()
    {
        _isPlaying = false;
        foreach (var player in _players)
        {
            player.Stop();
        }
    }

    /// <summary>
    /// Obtiene la lista de players registrados
    /// </summary>
    public IReadOnlyList<PrecisionVideoPlayer> Players => _players.AsReadOnly();

    // Eventos para coordinación nativa
    public event EventHandler<SyncPlayEventArgs>? SyncPlayRequested;
    public event EventHandler<PrecisionVideoPlayer[]>? SyncPauseRequested;
    public event EventHandler<SyncSeekEventArgs>? SyncSeekRequested;
}

/// <summary>
/// Argumentos para el evento de play sincronizado
/// </summary>
public class SyncPlayEventArgs : EventArgs
{
    public PrecisionVideoPlayer[] Players { get; }
    public TimeSpan StartPosition { get; }
    public double Speed { get; }

    public SyncPlayEventArgs(PrecisionVideoPlayer[] players, TimeSpan startPosition, double speed)
    {
        Players = players;
        StartPosition = startPosition;
        Speed = speed;
    }
}

/// <summary>
/// Argumentos para el evento de seek sincronizado
/// </summary>
public class SyncSeekEventArgs : EventArgs
{
    public PrecisionVideoPlayer[] Players { get; }
    public TimeSpan Position { get; }

    public SyncSeekEventArgs(PrecisionVideoPlayer[] players, TimeSpan position)
    {
        Players = players;
        Position = position;
    }
}
