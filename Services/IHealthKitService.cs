using System;
using System.Threading.Tasks;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Datos de salud del día para mostrar en el diario
/// </summary>
public class DailyHealthData
{
    /// <summary>Fecha de los datos</summary>
    public DateTime Date { get; set; }
    
    // Actividad
    /// <summary>Pasos totales del día</summary>
    public int Steps { get; set; }
    
    /// <summary>Calorías activas quemadas (kcal)</summary>
    public double ActiveCalories { get; set; }
    
    /// <summary>Tiempo de ejercicio en minutos</summary>
    public int ExerciseMinutes { get; set; }
    
    /// <summary>Distancia recorrida en km</summary>
    public double DistanceKm { get; set; }
    
    // Frecuencia cardíaca
    /// <summary>FC en reposo (bpm)</summary>
    public int? RestingHeartRate { get; set; }
    
    /// <summary>FC media del día (bpm)</summary>
    public int? AverageHeartRate { get; set; }
    
    /// <summary>FC máxima del día (bpm)</summary>
    public int? MaxHeartRate { get; set; }
    
    /// <summary>Variabilidad de FC (ms) - indicador de recuperación</summary>
    public double? HeartRateVariability { get; set; }
    
    // Sueño
    /// <summary>Horas de sueño de la noche anterior</summary>
    public double? SleepHours { get; set; }
    
    /// <summary>Calidad del sueño (0-100)</summary>
    public int? SleepQuality { get; set; }
    
    // Entrenamientos
    /// <summary>Número de entrenamientos registrados</summary>
    public int WorkoutCount { get; set; }
    
    /// <summary>Duración total de entrenamientos en minutos</summary>
    public int TotalWorkoutMinutes { get; set; }
    
    /// <summary>Tipos de entrenamiento realizados</summary>
    public List<string> WorkoutTypes { get; set; } = new();
    
    // Estado
    /// <summary>Indica si los datos están disponibles</summary>
    public bool HasData => Steps > 0 || ExerciseMinutes > 0 || RestingHeartRate.HasValue || SleepHours.HasValue;
    
    /// <summary>Indica si hay datos de frecuencia cardíaca</summary>
    public bool HasHeartRateData => RestingHeartRate.HasValue || AverageHeartRate.HasValue;
    
    /// <summary>Indica si hay datos de sueño</summary>
    public bool HasSleepData => SleepHours.HasValue;
    
    /// <summary>Indica si hay entrenamientos</summary>
    public bool HasWorkouts => WorkoutCount > 0;
    
    /// <summary>Texto formateado de sueño</summary>
    public string SleepFormatted => SleepHours.HasValue 
        ? $"{(int)SleepHours.Value}h {(int)((SleepHours.Value % 1) * 60)}m" 
        : "Sin datos";
    
    /// <summary>Indicador de recuperación basado en HRV y sueño (0-100)</summary>
    public int RecoveryScore
    {
        get
        {
            // Cálculo simplificado de score de recuperación
            var score = 50; // Base
            
            // HRV alta = buena recuperación
            if (HeartRateVariability.HasValue)
            {
                if (HeartRateVariability >= 50) score += 20;
                else if (HeartRateVariability >= 30) score += 10;
                else score -= 10;
            }
            
            // Sueño
            if (SleepHours.HasValue)
            {
                if (SleepHours >= 7) score += 20;
                else if (SleepHours >= 6) score += 10;
                else score -= 15;
            }
            
            // FC en reposo baja = buena forma
            if (RestingHeartRate.HasValue)
            {
                if (RestingHeartRate <= 55) score += 10;
                else if (RestingHeartRate <= 65) score += 5;
                else if (RestingHeartRate >= 80) score -= 10;
            }
            
            return Math.Clamp(score, 0, 100);
        }
    }
    
    /// <summary>Texto descriptivo del estado de recuperación</summary>
    public string RecoveryStatus
    {
        get
        {
            var score = RecoveryScore;
            return score switch
            {
                >= 80 => "Óptimo",
                >= 60 => "Bueno",
                >= 40 => "Normal",
                >= 20 => "Bajo",
                _ => "Muy bajo"
            };
        }
    }
    
    /// <summary>Color del indicador de recuperación</summary>
    public string RecoveryColor
    {
        get
        {
            var score = RecoveryScore;
            return score switch
            {
                >= 80 => "#FF4CAF50", // Verde
                >= 60 => "#FF8BC34A", // Verde claro
                >= 40 => "#FFFFEB3B", // Amarillo
                >= 20 => "#FFFF9800", // Naranja
                _ => "#FFF44336"      // Rojo
            };
        }
    }
}

/// <summary>
/// Interfaz para el servicio de HealthKit
/// </summary>
public interface IHealthKitService
{
    /// <summary>Indica si HealthKit está disponible en este dispositivo</summary>
    bool IsAvailable { get; }
    
    /// <summary>Indica si se ha concedido autorización</summary>
    bool IsAuthorized { get; }
    
    /// <summary>Solicita autorización para leer datos de salud</summary>
    Task<bool> RequestAuthorizationAsync();
    
    /// <summary>Obtiene los datos de salud de un día específico</summary>
    Task<DailyHealthData> GetHealthDataForDateAsync(DateTime date);
    
    /// <summary>Obtiene los datos de salud de los últimos N días</summary>
    Task<List<DailyHealthData>> GetHealthDataForPeriodAsync(DateTime startDate, DateTime endDate);
}
