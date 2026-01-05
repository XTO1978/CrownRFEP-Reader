using SQLite;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Almacena las últimas configuraciones de parciales utilizadas.
/// Permite reutilizar configuraciones previas en cualquier sesión.
/// </summary>
[Table("lap_config_history")]
public class LapConfigHistory
{
    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }
    
    /// <summary>Número de parciales configurados (1-10)</summary>
    [Column("lap_count")]
    public int LapCount { get; set; }
    
    /// <summary>
    /// Nombres de los parciales separados por pipe (|).
    /// Ejemplo: "Salida|Vuelta 1|Vuelta 2|Llegada"
    /// </summary>
    [Column("lap_names")]
    public string? LapNames { get; set; }
    
    /// <summary>Fecha de último uso (Unix timestamp)</summary>
    [Column("last_used")]
    public long LastUsed { get; set; }
    
    /// <summary>Número de veces que se ha usado esta configuración</summary>
    [Column("use_count")]
    public int UseCount { get; set; } = 1;
    
    // Propiedades computadas (no almacenadas)
    
    [Ignore]
    public DateTime LastUsedDateTime => DateTimeOffset.FromUnixTimeSeconds(LastUsed).LocalDateTime;
    
    [Ignore]
    public List<string> LapNamesList
    {
        get => string.IsNullOrEmpty(LapNames) 
            ? new List<string>() 
            : LapNames.Split('|').ToList();
        set => LapNames = value != null ? string.Join("|", value) : null;
    }
    
    /// <summary>Descripción corta para mostrar en la UI</summary>
    [Ignore]
    public string DisplaySummary
    {
        get
        {
            var names = LapNamesList;
            if (names.Count == 0) return $"{LapCount} parciales";
            
            // Mostrar los primeros 2-3 nombres
            var preview = names.Take(3).ToList();
            var suffix = names.Count > 3 ? $"... (+{names.Count - 3})" : "";
            return $"{string.Join(", ", preview)}{suffix}";
        }
    }
    
    /// <summary>
    /// Genera una clave única basada en la cantidad y nombres de los parciales
    /// para detectar configuraciones duplicadas.
    /// </summary>
    [Ignore]
    public string UniqueKey => $"{LapCount}|{LapNames ?? ""}";
}
