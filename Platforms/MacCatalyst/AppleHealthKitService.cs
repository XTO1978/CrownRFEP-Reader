using System;
using System.Threading.Tasks;
using HealthKit;
using Foundation;
using UIKit;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Implementación de HealthKit para macOS Catalyst / iOS
/// </summary>
public class AppleHealthKitService : IHealthKitService
{
    private readonly HKHealthStore _healthStore;
    private bool _isAuthorized;
    
    public bool IsAvailable => HKHealthStore.IsHealthDataAvailable;
    public bool IsAuthorized => _isAuthorized;
    
    // Tipos de datos que queremos leer
    private static readonly HKQuantityType? StepsType = HKQuantityType.Create(HKQuantityTypeIdentifier.StepCount);
    private static readonly HKQuantityType? ActiveCaloriesType = HKQuantityType.Create(HKQuantityTypeIdentifier.ActiveEnergyBurned);
    private static readonly HKQuantityType? DistanceType = HKQuantityType.Create(HKQuantityTypeIdentifier.DistanceWalkingRunning);
    private static readonly HKQuantityType? HeartRateType = HKQuantityType.Create(HKQuantityTypeIdentifier.HeartRate);
    private static readonly HKQuantityType? RestingHeartRateType = HKQuantityType.Create(HKQuantityTypeIdentifier.RestingHeartRate);
    private static readonly HKCategoryType? SleepType = HKCategoryType.Create(HKCategoryTypeIdentifier.SleepAnalysis);
    private static readonly HKQuantityType? ExerciseTimeType = HKQuantityType.Create(HKQuantityTypeIdentifier.AppleExerciseTime);
    
    // Unidades
    private static readonly HKUnit CountUnit = HKUnit.Count;
    private static readonly HKUnit KcalUnit = HKUnit.Kilocalorie;
    private static readonly HKUnit KmUnit = HKUnit.CreateMeterUnit(HKMetricPrefix.Kilo);
    private static readonly HKUnit MinuteUnit = HKUnit.Minute;
    private static readonly HKUnit BpmUnit = HKUnit.Count.UnitDividedBy(HKUnit.Minute);
    
    public AppleHealthKitService()
    {
        _healthStore = new HKHealthStore();
    }
    
    public async Task<bool> RequestAuthorizationAsync()
    {
        if (!IsAvailable)
        {
            System.Diagnostics.Debug.WriteLine("HealthKit no está disponible en este dispositivo");
            return false;
        }
        
        try
        {
            // Tipos que queremos leer
            var typesToRead = new List<HKObjectType>();
            if (StepsType != null) typesToRead.Add(StepsType);
            if (ActiveCaloriesType != null) typesToRead.Add(ActiveCaloriesType);
            if (DistanceType != null) typesToRead.Add(DistanceType);
            if (HeartRateType != null) typesToRead.Add(HeartRateType);
            if (RestingHeartRateType != null) typesToRead.Add(RestingHeartRateType);
            if (SleepType != null) typesToRead.Add(SleepType);
            if (ExerciseTimeType != null) typesToRead.Add(ExerciseTimeType);
            
            var readSet = new NSSet<HKObjectType>(typesToRead.ToArray());
            
            // No escribimos datos, solo leemos
            var writeSet = new NSSet<HKSampleType>();
            
            var tcs = new TaskCompletionSource<bool>();
            
            _healthStore.RequestAuthorizationToShare(
                writeSet,
                readSet,
                (success, error) =>
                {
                    if (error != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error solicitando autorización HealthKit: {error.LocalizedDescription}");
                        tcs.SetResult(false);
                    }
                    else
                    {
                        _isAuthorized = success;
                        tcs.SetResult(success);
                    }
                });
            
            return await tcs.Task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Excepción en RequestAuthorizationAsync: {ex}");
            return false;
        }
    }
    
    public async Task<DailyHealthData> GetHealthDataForDateAsync(DateTime date)
    {
        var data = new DailyHealthData { Date = date.Date };
        
        if (!IsAvailable)
            return data;
        
        var startOfDay = date.Date;
        var endOfDay = startOfDay.AddDays(1).AddSeconds(-1);
        
        try
        {
            // Ejecutar consultas
            if (StepsType != null)
                data.Steps = (int)await GetSumQuantityAsync(StepsType, startOfDay, endOfDay, CountUnit);
            
            if (ActiveCaloriesType != null)
                data.ActiveCalories = await GetSumQuantityAsync(ActiveCaloriesType, startOfDay, endOfDay, KcalUnit);
            
            if (DistanceType != null)
                data.DistanceKm = await GetSumQuantityAsync(DistanceType, startOfDay, endOfDay, KmUnit);
            
            if (ExerciseTimeType != null)
                data.ExerciseMinutes = (int)await GetSumQuantityAsync(ExerciseTimeType, startOfDay, endOfDay, MinuteUnit);
            
            if (RestingHeartRateType != null)
            {
                var restingHR = await GetAverageQuantityAsync(RestingHeartRateType, startOfDay, endOfDay, BpmUnit);
                data.RestingHeartRate = restingHR > 0 ? (int?)restingHR : null;
            }
            
            if (HeartRateType != null)
            {
                var avgHR = await GetAverageQuantityAsync(HeartRateType, startOfDay, endOfDay, BpmUnit);
                var maxHR = await GetMaxQuantityAsync(HeartRateType, startOfDay, endOfDay, BpmUnit);
                data.AverageHeartRate = avgHR > 0 ? (int?)avgHR : null;
                data.MaxHeartRate = maxHR > 0 ? (int?)maxHR : null;
            }
            
            if (SleepType != null)
            {
                var sleepHours = await GetSleepHoursAsync(startOfDay.AddDays(-1), startOfDay);
                data.SleepHours = sleepHours > 0 ? sleepHours : null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error obteniendo datos de HealthKit: {ex}");
        }
        
        return data;
    }
    
    public async Task<List<DailyHealthData>> GetHealthDataForPeriodAsync(DateTime startDate, DateTime endDate)
    {
        var results = new List<DailyHealthData>();
        
        for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
        {
            var dayData = await GetHealthDataForDateAsync(date);
            results.Add(dayData);
        }
        
        return results;
    }
    
    private async Task<double> GetSumQuantityAsync(HKQuantityType type, DateTime start, DateTime end, HKUnit unit)
    {
        var tcs = new TaskCompletionSource<double>();
        
        var predicate = HKQuery.GetPredicateForSamples(
            DateTimeToNSDate(start),
            DateTimeToNSDate(end),
            HKQueryOptions.StrictStartDate);
        
        var query = new HKStatisticsQuery(
            type,
            predicate,
            HKStatisticsOptions.CumulativeSum,
            (_, result, error) =>
            {
                if (error != null || result?.SumQuantity() == null)
                {
                    tcs.SetResult(0);
                }
                else
                {
                    tcs.SetResult(result.SumQuantity()!.GetDoubleValue(unit));
                }
            });
        
        _healthStore.ExecuteQuery(query);
        
        return await tcs.Task;
    }
    
    private async Task<double> GetAverageQuantityAsync(HKQuantityType type, DateTime start, DateTime end, HKUnit unit)
    {
        var tcs = new TaskCompletionSource<double>();
        
        var predicate = HKQuery.GetPredicateForSamples(
            DateTimeToNSDate(start),
            DateTimeToNSDate(end),
            HKQueryOptions.StrictStartDate);
        
        var query = new HKStatisticsQuery(
            type,
            predicate,
            HKStatisticsOptions.DiscreteAverage,
            (_, result, error) =>
            {
                if (error != null || result?.AverageQuantity() == null)
                {
                    tcs.SetResult(0);
                }
                else
                {
                    tcs.SetResult(result.AverageQuantity()!.GetDoubleValue(unit));
                }
            });
        
        _healthStore.ExecuteQuery(query);
        
        return await tcs.Task;
    }
    
    private async Task<double> GetMaxQuantityAsync(HKQuantityType type, DateTime start, DateTime end, HKUnit unit)
    {
        var tcs = new TaskCompletionSource<double>();
        
        var predicate = HKQuery.GetPredicateForSamples(
            DateTimeToNSDate(start),
            DateTimeToNSDate(end),
            HKQueryOptions.StrictStartDate);
        
        var query = new HKStatisticsQuery(
            type,
            predicate,
            HKStatisticsOptions.DiscreteMax,
            (_, result, error) =>
            {
                if (error != null || result?.MaximumQuantity() == null)
                {
                    tcs.SetResult(0);
                }
                else
                {
                    tcs.SetResult(result.MaximumQuantity()!.GetDoubleValue(unit));
                }
            });
        
        _healthStore.ExecuteQuery(query);
        
        return await tcs.Task;
    }
    
    private async Task<double> GetSleepHoursAsync(DateTime start, DateTime end)
    {
        if (SleepType == null)
            return 0;
            
        var tcs = new TaskCompletionSource<double>();
        
        var predicate = HKQuery.GetPredicateForSamples(
            DateTimeToNSDate(start),
            DateTimeToNSDate(end),
            HKQueryOptions.StrictStartDate);
        
        var sortDescriptor = new NSSortDescriptor(HKSample.SortIdentifierStartDate, false);
        
        var query = new HKSampleQuery(
            SleepType,
            predicate,
            0, // Sin límite
            new[] { sortDescriptor },
            (_, samples, error) =>
            {
                if (error != null || samples == null || samples.Length == 0)
                {
                    tcs.SetResult(0);
                    return;
                }
                
                // Sumar el tiempo de sueño (excluyendo "en cama" sin dormir)
                double totalSleepSeconds = 0;
                foreach (var sample in samples.OfType<HKCategorySample>())
                {
                    // Solo contar sueño real, no tiempo "en cama"
                    // HKCategoryValueSleepAnalysis.Asleep = 1
                    if (sample.Value >= 1)
                    {
                        var duration = sample.EndDate.SecondsSinceReferenceDate - sample.StartDate.SecondsSinceReferenceDate;
                        totalSleepSeconds += duration;
                    }
                }
                
                tcs.SetResult(totalSleepSeconds / 3600.0); // Convertir a horas
            });
        
        _healthStore.ExecuteQuery(query);
        
        return await tcs.Task;
    }
    
    private static NSDate DateTimeToNSDate(DateTime dateTime)
    {
        var reference = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var utcDateTime = dateTime.ToUniversalTime();
        return NSDate.FromTimeIntervalSinceReferenceDate((utcDateTime - reference).TotalSeconds);
    }
}
