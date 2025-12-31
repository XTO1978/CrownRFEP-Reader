using System;
using System.Threading.Tasks;
using HealthKit;
using Foundation;
using UIKit;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Implementación de HealthKit para iOS
/// </summary>
public class AppleHealthKitService : IHealthKitService
{
    private HKHealthStore? _healthStore;
    private bool _isAuthorized;
    private bool _isInitialized;
    
    public bool IsAvailable
    {
        get
        {
            try
            {
                return HKHealthStore.IsHealthDataAvailable;
            }
            catch
            {
                return false;
            }
        }
    }
    
    public bool IsAuthorized => _isAuthorized;
    
    // Tipos de datos - inicialización lazy para evitar crashes
    private HKQuantityType? _stepsType;
    private HKQuantityType? _activeCaloriesType;
    private HKQuantityType? _distanceType;
    private HKQuantityType? _heartRateType;
    private HKQuantityType? _restingHeartRateType;
    private HKCategoryType? _sleepType;
    private HKQuantityType? _exerciseTimeType;
    private HKQuantityType? _hrvType;
    
    // Unidades - inicialización lazy
    private HKUnit? _countUnit;
    private HKUnit? _kcalUnit;
    private HKUnit? _kmUnit;
    private HKUnit? _minuteUnit;
    private HKUnit? _bpmUnit;
    private HKUnit? _msUnit;
    
    public AppleHealthKitService()
    {
        // No inicializar nada en el constructor para evitar crashes
    }
    
    private void EnsureInitialized()
    {
        if (_isInitialized) return;
        
        try
        {
            if (!IsAvailable) return;
            
            _healthStore = new HKHealthStore();
            
            // Tipos de datos
            _stepsType = HKQuantityType.Create(HKQuantityTypeIdentifier.StepCount);
            _activeCaloriesType = HKQuantityType.Create(HKQuantityTypeIdentifier.ActiveEnergyBurned);
            _distanceType = HKQuantityType.Create(HKQuantityTypeIdentifier.DistanceWalkingRunning);
            _heartRateType = HKQuantityType.Create(HKQuantityTypeIdentifier.HeartRate);
            _restingHeartRateType = HKQuantityType.Create(HKQuantityTypeIdentifier.RestingHeartRate);
            _sleepType = HKCategoryType.Create(HKCategoryTypeIdentifier.SleepAnalysis);
            _exerciseTimeType = HKQuantityType.Create(HKQuantityTypeIdentifier.AppleExerciseTime);
            _hrvType = HKQuantityType.Create(HKQuantityTypeIdentifier.HeartRateVariabilitySdnn);
            
            // Unidades
            _countUnit = HKUnit.Count;
            _kcalUnit = HKUnit.Kilocalorie;
            _kmUnit = HKUnit.CreateMeterUnit(HKMetricPrefix.Kilo);
            _minuteUnit = HKUnit.Minute;
            _bpmUnit = HKUnit.Count.UnitDividedBy(HKUnit.Minute);
            _msUnit = HKUnit.CreateSecondUnit(HKMetricPrefix.Milli);
            
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error inicializando HealthKit: {ex}");
        }
    }
    
    public async Task<bool> RequestAuthorizationAsync()
    {
        if (!IsAvailable)
        {
            System.Diagnostics.Debug.WriteLine("HealthKit no está disponible en este dispositivo");
            return false;
        }
        
        EnsureInitialized();
        if (_healthStore == null) return false;
        
        try
        {
            // Tipos que queremos leer
            var typesToRead = new List<HKObjectType>();
            if (_stepsType != null) typesToRead.Add(_stepsType);
            if (_activeCaloriesType != null) typesToRead.Add(_activeCaloriesType);
            if (_distanceType != null) typesToRead.Add(_distanceType);
            if (_heartRateType != null) typesToRead.Add(_heartRateType);
            if (_restingHeartRateType != null) typesToRead.Add(_restingHeartRateType);
            if (_sleepType != null) typesToRead.Add(_sleepType);
            if (_exerciseTimeType != null) typesToRead.Add(_exerciseTimeType);
            if (_hrvType != null) typesToRead.Add(_hrvType);
            
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
        
        EnsureInitialized();
        if (_healthStore == null)
            return data;
        
        var startOfDay = date.Date;
        var endOfDay = startOfDay.AddDays(1).AddSeconds(-1);
        
        try
        {
            // Ejecutar consultas
            if (_stepsType != null && _countUnit != null)
                data.Steps = (int)await GetSumQuantityAsync(_stepsType, startOfDay, endOfDay, _countUnit);
            
            if (_activeCaloriesType != null && _kcalUnit != null)
                data.ActiveCalories = await GetSumQuantityAsync(_activeCaloriesType, startOfDay, endOfDay, _kcalUnit);
            
            if (_distanceType != null && _kmUnit != null)
                data.DistanceKm = await GetSumQuantityAsync(_distanceType, startOfDay, endOfDay, _kmUnit);
            
            if (_exerciseTimeType != null && _minuteUnit != null)
                data.ExerciseMinutes = (int)await GetSumQuantityAsync(_exerciseTimeType, startOfDay, endOfDay, _minuteUnit);
            
            if (_restingHeartRateType != null && _bpmUnit != null)
            {
                var restingHR = await GetAverageQuantityAsync(_restingHeartRateType, startOfDay, endOfDay, _bpmUnit);
                data.RestingHeartRate = restingHR > 0 ? (int?)restingHR : null;
            }
            
            if (_heartRateType != null && _bpmUnit != null)
            {
                var avgHR = await GetAverageQuantityAsync(_heartRateType, startOfDay, endOfDay, _bpmUnit);
                var maxHR = await GetMaxQuantityAsync(_heartRateType, startOfDay, endOfDay, _bpmUnit);
                data.AverageHeartRate = avgHR > 0 ? (int?)avgHR : null;
                data.MaxHeartRate = maxHR > 0 ? (int?)maxHR : null;
            }
            
            if (_hrvType != null && _msUnit != null)
            {
                var hrv = await GetAverageQuantityAsync(_hrvType, startOfDay, endOfDay, _msUnit);
                data.HeartRateVariability = hrv > 0 ? hrv : null;
            }
            
            if (_sleepType != null)
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
        if (_healthStore == null) return 0;
        
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
        if (_healthStore == null) return 0;
        
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
        if (_healthStore == null) return 0;
        
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
        if (_sleepType == null || _healthStore == null)
            return 0;
            
        var tcs = new TaskCompletionSource<double>();
        
        var predicate = HKQuery.GetPredicateForSamples(
            DateTimeToNSDate(start),
            DateTimeToNSDate(end),
            HKQueryOptions.StrictStartDate);
        
        var sortDescriptor = new NSSortDescriptor(HKSample.SortIdentifierStartDate, false);
        
        var query = new HKSampleQuery(
            _sleepType,
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
