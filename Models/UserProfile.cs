using SQLite;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Perfil del usuario de la aplicación
/// </summary>
[Table("userProfile")]
public class UserProfile
{
    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombre")]
    public string? Nombre { get; set; }

    [Column("apellidos")]
    public string? Apellidos { get; set; }

    [Column("fechaNacimiento")]
    public string? FechaNacimiento { get; set; }

    [Column("peso")]
    public double? Peso { get; set; } // en kg

    [Column("altura")]
    public double? Altura { get; set; } // en cm

    [Column("categoria")]
    public string? Categoria { get; set; } // Multiselección: valores separados por ';' (ej: "K1;C1")

    [Column("sexo")]
    public string? Sexo { get; set; } // Masculino, Femenino, Otro

    [Column("club")]
    public string? Club { get; set; }

    [Column("referenceAthleteId")]
    public int? ReferenceAthleteId { get; set; }

    [Column("fotoPath")]
    public string? FotoPath { get; set; }

    [Column("manoHabil")]
    public string? ManoHabil { get; set; } // Diestro, Zurdo

    [Column("notas")]
    public string? Notas { get; set; }

    [Column("createdAt")]
    public string? CreatedAt { get; set; }

    [Column("updatedAt")]
    public string? UpdatedAt { get; set; }

    // Propiedades calculadas (no mapeadas a DB)
    [Ignore]
    public string NombreCompleto => $"{Nombre} {Apellidos}".Trim();

    [Ignore]
    public DateTime? FechaNacimientoDateTime
    {
        get
        {
            if (string.IsNullOrEmpty(FechaNacimiento)) return null;
            if (DateTime.TryParse(FechaNacimiento, out var dt)) return dt;
            return null;
        }
        set => FechaNacimiento = value?.ToString("yyyy-MM-dd");
    }

    [Ignore]
    public int? Edad
    {
        get
        {
            var fechaNac = FechaNacimientoDateTime;
            if (!fechaNac.HasValue) return null;
            var today = DateTime.Today;
            var age = today.Year - fechaNac.Value.Year;
            if (fechaNac.Value.Date > today.AddYears(-age)) age--;
            return age;
        }
    }

    [Ignore]
    public double? IMC
    {
        get
        {
            if (!Peso.HasValue || !Altura.HasValue || Altura.Value <= 0) return null;
            var alturaMetros = Altura.Value / 100.0;
            return Math.Round(Peso.Value / (alturaMetros * alturaMetros), 1);
        }
    }
}

/// <summary>
/// Categorías de piragüismo disponibles
/// </summary>
public static class PaddlingCategories
{
    public static readonly string[] All = new[]
    {
        "K1",
        "C1",
        "K1X"
    };
}

/// <summary>
/// Opciones de sexo
/// </summary>
public static class SexOptions
{
    public static readonly string[] All = new[]
    {
        "Femenino",
        "Masculino",
        "Indiferente"
    };
}

/// <summary>
/// Opciones de mano hábil
/// </summary>
public static class HandOptions
{
    public static readonly string[] All = new[]
    {
        "Zurdo",
        "Diestro",
        "Indistinto"
    };
}

/// <summary>
/// Opción seleccionable para listas de opciones
/// </summary>
public class SelectableOption : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isSelected;
    
    public string Value { get; set; } = "";
    
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
