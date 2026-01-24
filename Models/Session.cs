using SQLite;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa una sesión de entrenamiento
/// </summary>
[Table("sesion")]
public class Session : INotifyPropertyChanged
{
    private string _icon = "oar.2.crossed";
    private string _iconColor = "#FFFFFFFF";
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("fecha")]
    public long Fecha { get; set; }

    [Column("lugar")]
    public string? Lugar { get; set; }

    [Column("tipoSesion")]
    public string? TipoSesion { get; set; }

    [Column("nombreSesion")]
    public string? NombreSesion { get; set; }

    [Column("pathSesion")]
    public string? PathSesion { get; set; }

    [Column("participantes")]
    public string? Participantes { get; set; }

    [Column("coach")]
    public string? Coach { get; set; }

    [Column("isMerged")]
    public int IsMerged { get; set; }

    // Papelera (soft-delete)
    [Column("is_deleted")]
    public int IsDeleted { get; set; }

    [Column("deleted_at_utc")]
    public long DeletedAtUtc { get; set; }

    [Column("is_favorite")]
    public int IsFavorite { get; set; }

    // Propiedades computadas
    [Ignore]
    public DateTime FechaDateTime => DateTimeOffset.FromUnixTimeSeconds(Fecha).LocalDateTime;

    [Ignore]
    public string DisplayName => string.IsNullOrEmpty(NombreSesion) 
        ? $"Sesión {Id}" 
        : NombreSesion;

    [Ignore]
    public List<string> ParticipantesList => 
        string.IsNullOrEmpty(Participantes) 
            ? new List<string>() 
            : Participantes.Split(',').Select(p => p.Trim()).ToList();

    [Ignore]
    public int VideoCount { get; set; }

    [Ignore]
    public bool IsFavoriteFlag
    {
        get => IsFavorite == 1;
        set
        {
            var newValue = value ? 1 : 0;
            if (IsFavorite == newValue) return;
            IsFavorite = newValue;
            OnPropertyChanged();
        }
    }

    [Ignore]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// SF Symbol name for the session icon (stored in Preferences, not DB)
    /// </summary>
    [Ignore]
    public string Icon
    {
        get => _icon;
        set
        {
            if (_icon == value) return;
            _icon = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Hex color for the session icon (stored in Preferences, not DB)
    /// </summary>
    [Ignore]
    public string IconColor
    {
        get => _iconColor;
        set
        {
            if (_iconColor == value) return;
            _iconColor = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
