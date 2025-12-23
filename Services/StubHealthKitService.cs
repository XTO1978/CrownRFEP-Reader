using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Implementación vacía de HealthKit para plataformas donde no está disponible.
/// HealthKit solo funciona en iOS (iPhone/iPad), NO en macOS ni MacCatalyst.
/// </summary>
public class StubHealthKitService : IHealthKitService
{
    // HealthKit solo está disponible en iOS real (iPhone/iPad), no en Mac
    public bool IsAvailable => false;
    public bool IsAuthorized => false;
    
    public Task<bool> RequestAuthorizationAsync()
    {
        return Task.FromResult(false);
    }
    
    public Task<DailyHealthData> GetHealthDataForDateAsync(DateTime date)
    {
        return Task.FromResult(new DailyHealthData { Date = date });
    }
    
    public Task<List<DailyHealthData>> GetHealthDataForPeriodAsync(DateTime startDate, DateTime endDate)
    {
        return Task.FromResult(new List<DailyHealthData>());
    }
}
