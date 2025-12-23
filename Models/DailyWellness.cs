using SQLite;
using System;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Datos de bienestar diario introducidos manualmente por el usuario
/// </summary>
public class DailyWellness
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    /// <summary>
    /// Fecha del registro (solo fecha, sin hora)
    /// </summary>
    [Indexed]
    public DateTime Date { get; set; }
    
    /// <summary>
    /// Horas de sueño (0-24)
    /// </summary>
    public double? SleepHours { get; set; }
    
    /// <summary>
    /// Calidad del sueño (1-5 estrellas)
    /// </summary>
    public int? SleepQuality { get; set; }
    
    /// <summary>
    /// Sensación de recuperación/energía (1-10)
    /// </summary>
    public int? RecoveryFeeling { get; set; }
    
    /// <summary>
    /// Nivel de fatiga muscular (1-10, donde 1=sin fatiga, 10=muy fatigado)
    /// </summary>
    public int? MuscleFatigue { get; set; }
    
    /// <summary>
    /// Estado de ánimo (1-5)
    /// </summary>
    public int? MoodRating { get; set; }
    
    /// <summary>
    /// Peso en kg (opcional)
    /// </summary>
    public double? WeightKg { get; set; }
    
    /// <summary>
    /// Frecuencia cardíaca en reposo (si el usuario la conoce)
    /// </summary>
    public int? RestingHeartRate { get; set; }
    
    /// <summary>
    /// Variabilidad de frecuencia cardíaca (HRV) en ms
    /// Valores típicos: 20-100+ ms (mayor = mejor recuperación)
    /// </summary>
    public int? HeartRateVariability { get; set; }
    
    /// <summary>
    /// Notas adicionales sobre el estado físico
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Fecha de creación del registro
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// Fecha de última modificación
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    
    // Propiedades calculadas para la UI
    
    /// <summary>
    /// Indica si hay algún dato registrado
    /// </summary>
    [Ignore]
    public bool HasData => SleepHours.HasValue || RecoveryFeeling.HasValue || 
                           MuscleFatigue.HasValue || MoodRating.HasValue || 
                           RestingHeartRate.HasValue || HeartRateVariability.HasValue ||
                           !string.IsNullOrEmpty(Notes);
    
    /// <summary>
    /// Puntuación general de bienestar (0-100)
    /// </summary>
    [Ignore]
    public int WellnessScore
    {
        get
        {
            if (!HasData) return 0;
            
            double score = 0;
            int factors = 0;
            
            // Sueño: 7-9 horas es óptimo
            if (SleepHours.HasValue)
            {
                var sleepScore = SleepHours.Value switch
                {
                    >= 7 and <= 9 => 100,
                    >= 6 and < 7 => 80,
                    >= 9 and <= 10 => 85,
                    >= 5 and < 6 => 60,
                    > 10 => 70,
                    _ => 40
                };
                score += sleepScore * 0.25; // 25% del total
                factors++;
            }
            
            // Calidad del sueño
            if (SleepQuality.HasValue)
            {
                score += (SleepQuality.Value * 20) * 0.15; // 15% del total
                factors++;
            }
            
            // Recuperación (1-10 → 10-100)
            if (RecoveryFeeling.HasValue)
            {
                score += (RecoveryFeeling.Value * 10) * 0.25; // 25% del total
                factors++;
            }
            
            // Fatiga muscular (invertido: menos fatiga = mejor)
            if (MuscleFatigue.HasValue)
            {
                score += ((11 - MuscleFatigue.Value) * 10) * 0.20; // 20% del total
                factors++;
            }
            
            // Estado de ánimo
            if (MoodRating.HasValue)
            {
                score += (MoodRating.Value * 20) * 0.15; // 15% del total
                factors++;
            }
            
            // Normalizar si no tenemos todos los factores
            if (factors > 0 && factors < 5)
            {
                var weights = new[] { 0.25, 0.15, 0.25, 0.20, 0.15 };
                var usedWeight = factors switch
                {
                    1 => weights[0],
                    2 => weights[0] + weights[1],
                    3 => weights[0] + weights[1] + weights[2],
                    4 => weights[0] + weights[1] + weights[2] + weights[3],
                    _ => 1.0
                };
                score = score / usedWeight;
            }
            
            return Math.Clamp((int)Math.Round(score), 0, 100);
        }
    }
    
    /// <summary>
    /// Color del indicador de bienestar basado en la puntuación
    /// </summary>
    [Ignore]
    public string WellnessColor => WellnessScore switch
    {
        >= 80 => "#FF4CAF50", // Verde
        >= 60 => "#FF8BC34A", // Verde claro
        >= 40 => "#FFFFC107", // Amarillo
        >= 20 => "#FFFF9800", // Naranja
        _ => "#FFF44336"      // Rojo
    };
    
    /// <summary>
    /// Texto descriptivo del nivel de bienestar
    /// </summary>
    [Ignore]
    public string WellnessText => WellnessScore switch
    {
        >= 80 => "Excelente",
        >= 60 => "Bueno",
        >= 40 => "Regular",
        >= 20 => "Bajo",
        _ => "Muy bajo"
    };
    
    /// <summary>
    /// Formato de horas de sueño para mostrar
    /// </summary>
    [Ignore]
    public string SleepFormatted => SleepHours.HasValue 
        ? $"{SleepHours.Value:F1}h" 
        : "--";
    
    /// <summary>
    /// SF Symbol del estado de ánimo
    /// </summary>
    [Ignore]
    public string MoodSymbol => MoodRating switch
    {
        5 => "sparkles",
        4 => "sun.max.fill",
        3 => "cloud.sun.fill",
        2 => "cloud.fill",
        1 => "cloud.heavyrain.fill",
        _ => "questionmark.circle"
    };
    
    /// <summary>
    /// Color del estado de ánimo (para TintColor del SymbolIcon)
    /// </summary>
    [Ignore]
    public Color MoodColor => MoodRating switch
    {
        5 => Color.FromArgb("#FF4CD964"),
        4 => Color.FromArgb("#FF8BC34A"),
        3 => Color.FromArgb("#FFFFC107"),
        2 => Color.FromArgb("#FFFF9500"),
        1 => Color.FromArgb("#FFFF6B6B"),
        _ => Color.FromArgb("#FF888888")
    };
    
    /// <summary>
    /// Emoji del estado de ánimo (para compatibilidad)
    /// </summary>
    [Ignore]
    public string MoodEmoji => MoodRating switch
    {
        5 => "😄",
        4 => "🙂",
        3 => "😐",
        2 => "😔",
        1 => "😢",
        _ => "❓"
    };
}
