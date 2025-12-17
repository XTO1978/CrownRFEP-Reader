using SQLite;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa un grupo de trabajo
/// </summary>
[Table("groupsName")]
public class WorkGroup
{
    [PrimaryKey, AutoIncrement]
    [Column("ID")]
    public int Id { get; set; }

    [Column("groupName")]
    public string? GroupName { get; set; }

    [Column("IsSelected")]
    public int IsSelected { get; set; }
}

/// <summary>
/// Representa la relación entre atletas y grupos de trabajo
/// </summary>
[Table("gruposTrabajo")]
public class AthleteWorkGroup
{
    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("athleteID")]
    public int AthleteId { get; set; }

    [Column("grupoID")]
    public int GrupoId { get; set; }
}
