using SQLite;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa un tipo de input
/// </summary>
[Table("inputType")]
public class InputType
{
    [PrimaryKey, AutoIncrement]
    [Column("ID")]
    public int Id { get; set; }

    [Column("tipo_input")]
    public string? TipoInput { get; set; }
}
