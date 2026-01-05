using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Opciones de configuración para el informe de sesión.
/// Cada sección puede ser habilitada/deshabilitada por el usuario.
/// </summary>
public class ReportOptions : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    
    private bool _showSessionHeader = true;
    private bool _showTrainingSummary = true;
    private bool _showGroupConsistency = true;
    private bool _showMinMaxCharts = true;
    private bool _showTimeTables = true;
    private bool _showIndividualConsistency = true;
    private bool _showLapAnalysis = true;
    private bool _showRanking = true;
    private bool _showPenalties = true;
    private bool _showAthleteProfile = true;
    
    // ==================== SECCIÓN 1: ENCABEZADO DE SESIÓN ====================
    
    /// <summary>
    /// Mostrar información básica de la sesión (nombre, fecha, lugar, coach)
    /// </summary>
    public bool ShowSessionHeader
    {
        get => _showSessionHeader;
        set => SetProperty(ref _showSessionHeader, value);
    }
    
    // ==================== SECCIÓN 2: RESUMEN DEL ENTRENAMIENTO ====================
    
    /// <summary>
    /// Mostrar resumen: total mangas, atletas, duración total, media de tiempos
    /// </summary>
    public bool ShowTrainingSummary
    {
        get => _showTrainingSummary;
        set => SetProperty(ref _showTrainingSummary, value);
    }
    
    // ==================== SECCIÓN 3: MÉTRICAS DE CONSISTENCIA DEL GRUPO ====================
    
    /// <summary>
    /// Mostrar CV%, rango, media, desviación estándar y outliers del grupo
    /// </summary>
    public bool ShowGroupConsistency
    {
        get => _showGroupConsistency;
        set => SetProperty(ref _showGroupConsistency, value);
    }
    
    // ==================== SECCIÓN 4: GRÁFICOS MIN-MAX ====================
    
    /// <summary>
    /// Mostrar gráficos Min-Max normalizados (parciales y acumulados)
    /// </summary>
    public bool ShowMinMaxCharts
    {
        get => _showMinMaxCharts;
        set => SetProperty(ref _showMinMaxCharts, value);
    }
    
    // ==================== SECCIÓN 5: TABLAS DE TIEMPOS ====================
    
    /// <summary>
    /// Mostrar tablas de tiempos parciales y acumulados
    /// </summary>
    public bool ShowTimeTables
    {
        get => _showTimeTables;
        set => SetProperty(ref _showTimeTables, value);
    }
    
    // ==================== SECCIÓN 6: CONSISTENCIA INDIVIDUAL ====================
    
    /// <summary>
    /// Mostrar métricas de consistencia para atletas con múltiples intentos
    /// </summary>
    public bool ShowIndividualConsistency
    {
        get => _showIndividualConsistency;
        set => SetProperty(ref _showIndividualConsistency, value);
    }
    
    // ==================== SECCIÓN 7: ANÁLISIS DE PARCIALES ====================
    
    /// <summary>
    /// Mostrar análisis de dónde gana/pierde tiempo cada atleta por parcial
    /// </summary>
    public bool ShowLapAnalysis
    {
        get => _showLapAnalysis;
        set => SetProperty(ref _showLapAnalysis, value);
    }
    
    // ==================== SECCIÓN 8: RANKING ====================
    
    /// <summary>
    /// Mostrar ranking y posición respecto al grupo
    /// </summary>
    public bool ShowRanking
    {
        get => _showRanking;
        set => SetProperty(ref _showRanking, value);
    }
    
    // ==================== SECCIÓN 9: PENALIZACIONES ====================
    
    /// <summary>
    /// Mostrar análisis de penalizaciones (+2)
    /// </summary>
    public bool ShowPenalties
    {
        get => _showPenalties;
        set => SetProperty(ref _showPenalties, value);
    }
    
    // ==================== SECCIÓN 10: PERFIL DEL ATLETA ====================
    
    /// <summary>
    /// Mostrar perfil individual del atleta seleccionado
    /// </summary>
    public bool ShowAthleteProfile
    {
        get => _showAthleteProfile;
        set => SetProperty(ref _showAthleteProfile, value);
    }
    
    // ==================== HELPERS ====================
    
    /// <summary>
    /// Activa todas las secciones
    /// </summary>
    public void EnableAll()
    {
        ShowSessionHeader = true;
        ShowTrainingSummary = true;
        ShowGroupConsistency = true;
        ShowMinMaxCharts = true;
        ShowTimeTables = true;
        ShowIndividualConsistency = true;
        ShowLapAnalysis = true;
        ShowRanking = true;
        ShowPenalties = true;
        ShowAthleteProfile = true;
    }
    
    /// <summary>
    /// Desactiva todas las secciones
    /// </summary>
    public void DisableAll()
    {
        ShowSessionHeader = false;
        ShowTrainingSummary = false;
        ShowGroupConsistency = false;
        ShowMinMaxCharts = false;
        ShowTimeTables = false;
        ShowIndividualConsistency = false;
        ShowLapAnalysis = false;
        ShowRanking = false;
        ShowPenalties = false;
        ShowAthleteProfile = false;
    }
    
    /// <summary>
    /// Devuelve un preset "Resumen rápido" (solo lo esencial)
    /// </summary>
    public static ReportOptions QuickSummary() => new()
    {
        ShowSessionHeader = true,
        ShowTrainingSummary = true,
        ShowGroupConsistency = true,
        ShowMinMaxCharts = false,
        ShowTimeTables = true,
        ShowIndividualConsistency = false,
        ShowLapAnalysis = false,
        ShowRanking = true,
        ShowPenalties = false,
        ShowAthleteProfile = false
    };
    
    /// <summary>
    /// Devuelve un preset "Análisis completo" (todo)
    /// </summary>
    public static ReportOptions FullAnalysis()
    {
        var options = new ReportOptions();
        options.EnableAll();
        return options;
    }
    
    /// <summary>
    /// Devuelve un preset "Informe de atleta" (enfocado en un atleta)
    /// </summary>
    public static ReportOptions AthleteReport() => new()
    {
        ShowSessionHeader = true,
        ShowTrainingSummary = false,
        ShowGroupConsistency = false,
        ShowMinMaxCharts = true,
        ShowTimeTables = true,
        ShowIndividualConsistency = true,
        ShowLapAnalysis = true,
        ShowRanking = true,
        ShowPenalties = true,
        ShowAthleteProfile = true
    };
    
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
