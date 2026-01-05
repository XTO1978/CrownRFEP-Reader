using SQLite;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Almacena la configuración de parciales del modo asistido para una sesión.
/// Cuando se abre un video de la sesión, se carga esta configuración.
/// </summary>
[Table("session_lap_config")]
public class SessionLapConfig
{
    [PrimaryKey]
    [Column("session_id")]
    public int SessionId { get; set; }
    
    /// <summary>Número de parciales configurados (1-10)</summary>
    [Column("lap_count")]
    public int LapCount { get; set; } = 3;
    
    /// <summary>
    /// Nombres de los parciales separados por pipe (|).
    /// Ejemplo: "Salida|Vuelta 1|Vuelta 2|Llegada"
    /// </summary>
    [Column("lap_names")]
    public string? LapNames { get; set; }
    
    /// <summary>Fecha de última modificación (Unix timestamp)</summary>
    [Column("last_modified")]
    public long LastModified { get; set; }
    
    // Propiedades computadas (no almacenadas)
    
    [Ignore]
    public DateTime LastModifiedDateTime => DateTimeOffset.FromUnixTimeSeconds(LastModified).LocalDateTime;
    
    [Ignore]
    public List<string> LapNamesList
    {
        get => string.IsNullOrEmpty(LapNames) 
            ? new List<string>() 
            : LapNames.Split('|').ToList();
        set => LapNames = value != null ? string.Join("|", value) : null;
    }
    
    /// <summary>
    /// Crea una configuración con nombres por defecto
    /// </summary>
    public static SessionLapConfig CreateDefault(int sessionId, int lapCount = 3)
    {
        var names = new List<string>();
        for (int i = 1; i <= lapCount; i++)
        {
            names.Add($"P{i}");
        }
        
        return new SessionLapConfig
        {
            SessionId = sessionId,
            LapCount = lapCount,
            LapNames = string.Join("|", names),
            LastModified = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }
}
