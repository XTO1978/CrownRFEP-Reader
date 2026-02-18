using System.Collections.ObjectModel;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using CrownRFEP_Reader.Views;
using Microsoft.Maui.Controls;

namespace CrownRFEP_Reader.ViewModels;

public class DiaryWellnessViewModel : ObservableObject
{
    private readonly DatabaseService _databaseService;
    private readonly IHealthKitService _healthKitService;
    private Func<Session?>? _getSelectedSession;
    private Action? _selectDiaryTabAction;

    // Vista Diario (calendario)
    private DateTime _selectedDiaryDate = DateTime.Today;
    private List<SessionDiary> _diaryEntriesForMonth = new();
    private List<Session> _sessionsForMonth = new();
    private List<DailyWellness> _wellnessDataForMonth = new();
    private SessionDiary? _selectedDateDiary;
    private ObservableCollection<SessionWithDiary> _selectedDateSessionsWithDiary = new();

    // HealthKit / Datos de Salud (solo iOS) y Bienestar manual
    private DailyHealthData? _selectedDateHealthData;
    private bool _isHealthKitAuthorized;
    private bool _isLoadingHealthData;

    // Bienestar diario (entrada manual)
    private DailyWellness? _selectedDateWellness;
    private bool _isEditingWellness;
    private double? _wellnessSleepHours;
    private int? _wellnessSleepQuality;
    private int? _wellnessRecoveryFeeling;
    private int? _wellnessMuscleFatigue;
    private int? _wellnessMoodRating;
    private int? _wellnessRestingHeartRate;
    private int? _wellnessHRV;
    private string? _wellnessNotes;

    // Diario de sesión
    private SessionDiary? _currentSessionDiary;
    private bool _isEditingDiary;
    private int _diaryValoracionFisica = 3;
    private int _diaryValoracionMental = 3;
    private int _diaryValoracionTecnica = 3;
    private string _diaryNotas = "";
    private double _avgValoracionFisica;
    private double _avgValoracionMental;
    private double _avgValoracionTecnica;
    private int _avgValoracionCount;
    private int _selectedEvolutionPeriod = 1; // 0=Semana, 1=Mes, 2=Año, 3=Todo
    private ObservableCollection<SessionDiary> _valoracionEvolution = new();

    // Datos para el gráfico de líneas múltiples
    private ObservableCollection<int> _evolutionFisicaValues = new();
    private ObservableCollection<int> _evolutionMentalValues = new();
    private ObservableCollection<int> _evolutionTecnicaValues = new();
    private ObservableCollection<string> _evolutionLabels = new();

    public DiaryWellnessViewModel(DatabaseService databaseService, IHealthKitService healthKitService)
    {
        _databaseService = databaseService;
        _healthKitService = healthKitService;

        SelectDiaryDateCommand = new RelayCommand<DateTime>(date => SelectedDiaryDate = date);
        SelectDiaryTabCommand = new RelayCommand(SelectDiaryTab);
        SaveDiaryCommand = new AsyncRelayCommand(SaveDiaryAsync);
        EditDiaryCommand = new RelayCommand(() => IsEditingDiary = true);
        SaveCalendarDiaryCommand = new AsyncRelayCommand<SessionWithDiary>(SaveCalendarDiaryAsync);
        ViewSessionAsPlaylistCommand = new AsyncRelayCommand<SessionWithDiary>(ViewSessionAsPlaylistAsync);
        ConnectHealthKitCommand = new AsyncRelayCommand(ConnectHealthKitAsync);
        ImportHealthDataToWellnessCommand = new AsyncRelayCommand(ImportHealthDataToWellnessAsync);
        SaveWellnessCommand = new AsyncRelayCommand(SaveWellnessAsync);
        StartEditWellnessCommand = new RelayCommand(() => IsEditingWellness = true);
        CancelEditWellnessCommand = new RelayCommand(CancelEditWellness);
        SetMoodCommand = new RelayCommand<string>(mood =>
        {
            if (int.TryParse(mood, out int moodValue))
                WellnessMoodRating = moodValue;
        });
        ShowPPMInfoCommand = new AsyncRelayCommand(ShowPPMInfoAsync);
        ShowHRVInfoCommand = new AsyncRelayCommand(ShowHRVInfoAsync);
        SetEvolutionPeriodCommand = new RelayCommand<string>(period =>
        {
            if (int.TryParse(period, out var p))
                SelectedEvolutionPeriod = p;
        });
    }

    public void Configure(Func<Session?> getSelectedSession, Action selectDiaryTabAction)
    {
        _getSelectedSession = getSelectedSession;
        _selectDiaryTabAction = selectDiaryTabAction;
    }

    public ICommand SelectDiaryDateCommand { get; }
    public ICommand SelectDiaryTabCommand { get; }
    public ICommand SaveDiaryCommand { get; }
    public ICommand EditDiaryCommand { get; }
    public ICommand SaveCalendarDiaryCommand { get; }
    public ICommand ViewSessionAsPlaylistCommand { get; }
    public ICommand ConnectHealthKitCommand { get; }
    public ICommand ImportHealthDataToWellnessCommand { get; }
    public ICommand SaveWellnessCommand { get; }
    public ICommand StartEditWellnessCommand { get; }
    public ICommand CancelEditWellnessCommand { get; }
    public ICommand SetMoodCommand { get; }
    public ICommand ShowPPMInfoCommand { get; }
    public ICommand ShowHRVInfoCommand { get; }
    public ICommand SetEvolutionPeriodCommand { get; }

    public DateTime SelectedDiaryDate
    {
        get => _selectedDiaryDate;
        set
        {
            if (SetProperty(ref _selectedDiaryDate, value))
            {
                _ = LoadDiaryForDateAsync(value);
                _ = LoadWellnessDataForDateAsync(value);
            }
        }
    }

    public List<SessionDiary> DiaryEntriesForMonth
    {
        get => _diaryEntriesForMonth;
        set => SetProperty(ref _diaryEntriesForMonth, value);
    }

    public List<Session> SessionsForMonth
    {
        get => _sessionsForMonth;
        set => SetProperty(ref _sessionsForMonth, value);
    }

    public List<DailyWellness> WellnessDataForMonth
    {
        get => _wellnessDataForMonth;
        set
        {
            if (SetProperty(ref _wellnessDataForMonth, value))
            {
                OnPropertyChanged(nameof(AverageWellnessScore));
            }
        }
    }

    /// <summary>
    /// Media del WellnessScore de todos los datos del mes
    /// </summary>
    public int? AverageWellnessScore
    {
        get
        {
            if (_wellnessDataForMonth == null || _wellnessDataForMonth.Count == 0)
                return null;

            var scoresWithData = _wellnessDataForMonth
                .Where(w => w.HasData && w.WellnessScore > 0)
                .Select(w => w.WellnessScore)
                .ToList();

            if (scoresWithData.Count == 0)
                return null;

            return (int)Math.Round(scoresWithData.Average());
        }
    }

    public SessionDiary? SelectedDateDiary
    {
        get => _selectedDateDiary;
        set
        {
            if (SetProperty(ref _selectedDateDiary, value))
            {
                OnPropertyChanged(nameof(HasSelectedDateDiary));
            }
        }
    }

    public bool HasSelectedDateDiary => SelectedDateDiary != null;

    public ObservableCollection<SessionWithDiary> SelectedDateSessionsWithDiary
    {
        get => _selectedDateSessionsWithDiary;
        set => SetProperty(ref _selectedDateSessionsWithDiary, value);
    }

    public bool HasSelectedDateSessions => SelectedDateSessionsWithDiary.Count > 0;

    // Propiedades HealthKit
    public DailyHealthData? SelectedDateHealthData
    {
        get => _selectedDateHealthData;
        set
        {
            if (SetProperty(ref _selectedDateHealthData, value))
            {
                OnPropertyChanged(nameof(HasHealthData));
            }
        }
    }

    public bool HasHealthData => SelectedDateHealthData?.HasData == true;
    public bool IsHealthKitAvailable => _healthKitService.IsAvailable;

    public bool IsHealthKitAuthorized
    {
        get => _isHealthKitAuthorized;
        set => SetProperty(ref _isHealthKitAuthorized, value);
    }

    public bool IsLoadingHealthData
    {
        get => _isLoadingHealthData;
        set => SetProperty(ref _isLoadingHealthData, value);
    }

    // Propiedades Bienestar Diario (entrada manual)
    public DailyWellness? SelectedDateWellness
    {
        get => _selectedDateWellness;
        set
        {
            if (SetProperty(ref _selectedDateWellness, value))
            {
                OnPropertyChanged(nameof(HasWellnessData));
                // Sincronizar campos de edición
                if (value != null)
                {
                    WellnessSleepHours = value.SleepHours;
                    WellnessSleepQuality = value.SleepQuality;
                    WellnessRecoveryFeeling = value.RecoveryFeeling;
                    WellnessMuscleFatigue = value.MuscleFatigue;
                    WellnessMoodRating = value.MoodRating;
                    WellnessRestingHeartRate = value.RestingHeartRate;
                    WellnessHRV = value.HeartRateVariability;
                    WellnessNotes = value.Notes;
                }
                else
                {
                    ClearWellnessFields();
                }
            }
        }
    }

    public bool HasWellnessData => SelectedDateWellness?.HasData == true;

    public bool IsEditingWellness
    {
        get => _isEditingWellness;
        set => SetProperty(ref _isEditingWellness, value);
    }

    public double? WellnessSleepHours
    {
        get => _wellnessSleepHours;
        set => SetProperty(ref _wellnessSleepHours, value);
    }

    public int? WellnessSleepQuality
    {
        get => _wellnessSleepQuality;
        set => SetProperty(ref _wellnessSleepQuality, value);
    }

    public int? WellnessRecoveryFeeling
    {
        get => _wellnessRecoveryFeeling;
        set => SetProperty(ref _wellnessRecoveryFeeling, value);
    }

    public int? WellnessMuscleFatigue
    {
        get => _wellnessMuscleFatigue;
        set => SetProperty(ref _wellnessMuscleFatigue, value);
    }

    public int? WellnessMoodRating
    {
        get => _wellnessMoodRating;
        set => SetProperty(ref _wellnessMoodRating, value);
    }

    public int? WellnessRestingHeartRate
    {
        get => _wellnessRestingHeartRate;
        set => SetProperty(ref _wellnessRestingHeartRate, value);
    }

    public int? WellnessHRV
    {
        get => _wellnessHRV;
        set => SetProperty(ref _wellnessHRV, value);
    }

    public string? WellnessNotes
    {
        get => _wellnessNotes;
        set => SetProperty(ref _wellnessNotes, value);
    }

    // Opciones para selectores de bienestar
    public List<int> SleepQualityOptions { get; } = new() { 1, 2, 3, 4, 5 };
    public List<int> RecoveryFeelingOptions { get; } = new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    public List<int> MuscleFatigueOptions { get; } = new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    public List<int> MoodRatingOptions { get; } = new() { 1, 2, 3, 4, 5 };

    // Propiedades del Diario de Sesión
    public int DiaryValoracionFisica
    {
        get => _diaryValoracionFisica;
        set => SetProperty(ref _diaryValoracionFisica, Math.Clamp(value, 1, 5));
    }

    public int DiaryValoracionMental
    {
        get => _diaryValoracionMental;
        set => SetProperty(ref _diaryValoracionMental, Math.Clamp(value, 1, 5));
    }

    public int DiaryValoracionTecnica
    {
        get => _diaryValoracionTecnica;
        set => SetProperty(ref _diaryValoracionTecnica, Math.Clamp(value, 1, 5));
    }

    public string DiaryNotas
    {
        get => _diaryNotas;
        set => SetProperty(ref _diaryNotas, value ?? "");
    }

    public double AvgValoracionFisica
    {
        get => _avgValoracionFisica;
        set => SetProperty(ref _avgValoracionFisica, value);
    }

    public double AvgValoracionMental
    {
        get => _avgValoracionMental;
        set => SetProperty(ref _avgValoracionMental, value);
    }

    public double AvgValoracionTecnica
    {
        get => _avgValoracionTecnica;
        set => SetProperty(ref _avgValoracionTecnica, value);
    }

    public int AvgValoracionCount
    {
        get => _avgValoracionCount;
        set => SetProperty(ref _avgValoracionCount, value);
    }

    public int SelectedEvolutionPeriod
    {
        get => _selectedEvolutionPeriod;
        set
        {
            if (SetProperty(ref _selectedEvolutionPeriod, value))
            {
                _ = LoadValoracionEvolutionAsync();
            }
        }
    }

    public ObservableCollection<SessionDiary> ValoracionEvolution
    {
        get => _valoracionEvolution;
        set => SetProperty(ref _valoracionEvolution, value);
    }

    public ObservableCollection<int> EvolutionFisicaValues
    {
        get => _evolutionFisicaValues;
        set => SetProperty(ref _evolutionFisicaValues, value);
    }

    public ObservableCollection<int> EvolutionMentalValues
    {
        get => _evolutionMentalValues;
        set => SetProperty(ref _evolutionMentalValues, value);
    }

    public ObservableCollection<int> EvolutionTecnicaValues
    {
        get => _evolutionTecnicaValues;
        set => SetProperty(ref _evolutionTecnicaValues, value);
    }

    public ObservableCollection<string> EvolutionLabels
    {
        get => _evolutionLabels;
        set => SetProperty(ref _evolutionLabels, value);
    }

    public bool HasDiaryData => _currentSessionDiary != null;

    /// <summary>Indica si el usuario está editando el diario (muestra formulario vs vista de resultados)</summary>
    public bool IsEditingDiary
    {
        get => _isEditingDiary;
        set => SetProperty(ref _isEditingDiary, value);
    }

    /// <summary>Indica si debe mostrarse la vista de resultados del diario (datos guardados y no editando)</summary>
    public bool ShowDiaryResults => HasDiaryData && !IsEditingDiary;

    /// <summary>Indica si debe mostrarse el formulario del diario (sin datos o editando)</summary>
    public bool ShowDiaryForm => !HasDiaryData || IsEditingDiary;

    public async Task LoadSessionDiaryAsync(Session? selectedSession)
    {
        if (selectedSession == null) return;

        try
        {
            // Obtener el atleta de referencia del perfil de usuario
            var profile = await _databaseService.GetUserProfileAsync();
            if (profile?.ReferenceAthleteId == null) return;

            var athleteId = profile.ReferenceAthleteId.Value;

            // Cargar diario existente o crear uno nuevo
            _currentSessionDiary = await _databaseService.GetSessionDiaryAsync(selectedSession.Id, athleteId);

            if (_currentSessionDiary != null)
            {
                DiaryValoracionFisica = _currentSessionDiary.ValoracionFisica;
                DiaryValoracionMental = _currentSessionDiary.ValoracionMental;
                DiaryValoracionTecnica = _currentSessionDiary.ValoracionTecnica;
                DiaryNotas = _currentSessionDiary.Notas ?? "";
                IsEditingDiary = false; // Hay datos, mostrar vista de resultados
            }
            else
            {
                // Valores por defecto
                DiaryValoracionFisica = 3;
                DiaryValoracionMental = 3;
                DiaryValoracionTecnica = 3;
                DiaryNotas = "";
                IsEditingDiary = true; // No hay datos, mostrar formulario
            }

            OnPropertyChanged(nameof(HasDiaryData));
            OnPropertyChanged(nameof(ShowDiaryResults));
            OnPropertyChanged(nameof(ShowDiaryForm));

            // Cargar promedios
            await LoadValoracionAveragesAsync(athleteId);

            // Cargar evolución
            await LoadValoracionEvolutionAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando diario: {ex}");
        }
    }

    public async Task LoadDiaryViewDataAsync()
    {
        try
        {
            var profile = await _databaseService.GetUserProfileAsync();
            if (profile?.ReferenceAthleteId == null) return;

            // Cargar entradas del mes actual
            await LoadDiaryEntriesForMonthAsync(SelectedDiaryDate, profile.ReferenceAthleteId.Value);

            // Cargar diario del día seleccionado
            await LoadDiaryForDateAsync(SelectedDiaryDate);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando vista diario: {ex}");
        }
    }

    public async Task RefreshDiaryForSelectedDateAsync()
    {
        var profile = await _databaseService.GetUserProfileAsync();
        if (profile?.ReferenceAthleteId == null) return;

        await LoadDiaryEntriesForMonthAsync(SelectedDiaryDate, profile.ReferenceAthleteId.Value);
        await LoadDiaryForDateAsync(SelectedDiaryDate);
    }

    public async Task LoadDiaryForDateAsync(DateTime date)
    {
        try
        {
            var profile = await _databaseService.GetUserProfileAsync();
            if (profile?.ReferenceAthleteId == null)
            {
                SelectedDateDiary = null;
                SelectedDateSessionsWithDiary.Clear();
                OnPropertyChanged(nameof(HasSelectedDateSessions));
                return;
            }

            var athleteId = profile.ReferenceAthleteId.Value;

            // Buscar un diario para esa fecha específica (mantener compatibilidad)
            var entries = await _databaseService.GetSessionDiariesForPeriodAsync(
                athleteId, date.Date, date.Date.AddDays(1).AddSeconds(-1));

            SelectedDateDiary = entries.FirstOrDefault();

            // Cargar todas las sesiones del día con sus diarios
            var sessionsForDate = SessionsForMonth
                .Where(s => s.FechaDateTime.Date == date.Date)
                .ToList();

            SelectedDateSessionsWithDiary.Clear();

            foreach (var session in sessionsForDate)
            {
                // Buscar el diario correspondiente a esta sesión
                var diary = DiaryEntriesForMonth.FirstOrDefault(d => d.SessionId == session.Id);

                // Cargar los vídeos de la sesión (máximo 6 para la mini galería)
                var allVideos = await _databaseService.GetVideoClipsBySessionAsync(session.Id);
                var previewVideos = allVideos.Take(6).ToList();

                var sessionWithDiary = new SessionWithDiary
                {
                    Session = session,
                    Diary = diary,
                    VideoCount = allVideos.Count
                };

                foreach (var video in previewVideos)
                    sessionWithDiary.Videos.Add(video);

                SelectedDateSessionsWithDiary.Add(sessionWithDiary);
            }

            OnPropertyChanged(nameof(HasSelectedDateSessions));

            // Cargar datos de salud si HealthKit está autorizado
            await LoadHealthDataForDateAsync(date);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando diario del día: {ex}");
            SelectedDateDiary = null;
            SelectedDateSessionsWithDiary.Clear();
            OnPropertyChanged(nameof(HasSelectedDateSessions));
        }
    }

    private void SelectDiaryTab()
    {
        _selectDiaryTabAction?.Invoke();
        var session = _getSelectedSession?.Invoke();
        if (session != null)
            _ = LoadSessionDiaryAsync(session);
    }

    private async Task LoadValoracionAveragesAsync(int athleteId)
    {
        try
        {
            var (fisica, mental, tecnica, count) = await _databaseService.GetValoracionAveragesAsync(athleteId, 30);
            AvgValoracionFisica = fisica;
            AvgValoracionMental = mental;
            AvgValoracionTecnica = tecnica;
            AvgValoracionCount = count;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando promedios: {ex}");
        }
    }

    private async Task LoadValoracionEvolutionAsync()
    {
        try
        {
            var profile = await _databaseService.GetUserProfileAsync();
            if (profile?.ReferenceAthleteId == null) return;

            var athleteId = profile.ReferenceAthleteId.Value;
            var now = DateTimeOffset.UtcNow;
            long startDate;

            switch (SelectedEvolutionPeriod)
            {
                case 0: // Semana
                    startDate = now.AddDays(-7).ToUnixTimeMilliseconds();
                    break;
                case 1: // Mes
                    startDate = now.AddMonths(-1).ToUnixTimeMilliseconds();
                    break;
                case 2: // Año
                    startDate = now.AddYears(-1).ToUnixTimeMilliseconds();
                    break;
                default: // Todo
                    startDate = 0;
                    break;
            }

            var endDate = now.ToUnixTimeMilliseconds();
            var evolution = await _databaseService.GetValoracionEvolutionAsync(athleteId, startDate, endDate);

            ValoracionEvolution = new ObservableCollection<SessionDiary>(evolution);

            // Poblar colecciones para el gráfico de evolución
            var fisicaValues = new ObservableCollection<int>();
            var mentalValues = new ObservableCollection<int>();
            var tecnicaValues = new ObservableCollection<int>();
            var labels = new ObservableCollection<string>();

            foreach (var diary in evolution.OrderBy(d => d.CreatedAt))
            {
                fisicaValues.Add(diary.ValoracionFisica);
                mentalValues.Add(diary.ValoracionMental);
                tecnicaValues.Add(diary.ValoracionTecnica);

                // Formatear fecha como etiqueta
                var date = DateTimeOffset.FromUnixTimeMilliseconds(diary.CreatedAt).LocalDateTime;
                labels.Add(date.ToString("dd/MM"));
            }

            EvolutionFisicaValues = fisicaValues;
            EvolutionMentalValues = mentalValues;
            EvolutionTecnicaValues = tecnicaValues;
            EvolutionLabels = labels;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando evolución: {ex}");
        }
    }

    private async Task SaveDiaryAsync()
    {
        var selectedSession = _getSelectedSession?.Invoke();
        if (selectedSession == null) return;

        try
        {
            var profile = await _databaseService.GetUserProfileAsync();
            if (profile?.ReferenceAthleteId == null)
            {
                await Shell.Current.DisplayAlert("Error", "No hay un atleta de referencia configurado en tu perfil.", "OK");
                return;
            }

            var athleteId = profile.ReferenceAthleteId.Value;

            var diary = new SessionDiary
            {
                SessionId = selectedSession.Id,
                AthleteId = athleteId,
                ValoracionFisica = DiaryValoracionFisica,
                ValoracionMental = DiaryValoracionMental,
                ValoracionTecnica = DiaryValoracionTecnica,
                Notas = DiaryNotas
            };

            await _databaseService.SaveSessionDiaryAsync(diary);
            _currentSessionDiary = diary;
            IsEditingDiary = false;
            OnPropertyChanged(nameof(HasDiaryData));
            OnPropertyChanged(nameof(ShowDiaryResults));
            OnPropertyChanged(nameof(ShowDiaryForm));

            // Recargar promedios y evolución
            await LoadValoracionAveragesAsync(athleteId);
            await LoadValoracionEvolutionAsync();

            await Shell.Current.DisplayAlert("Guardado", "Tu diario de sesión se ha guardado correctamente.", "OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error guardando diario: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo guardar el diario: {ex.Message}", "OK");
        }
    }

    private async Task SaveCalendarDiaryAsync(SessionWithDiary? sessionWithDiary)
    {
        if (sessionWithDiary == null) return;

        try
        {
            var profile = await _databaseService.GetUserProfileAsync();
            if (profile?.ReferenceAthleteId == null)
            {
                await Shell.Current.DisplayAlert("Error", "No hay un atleta de referencia configurado en tu perfil.", "OK");
                return;
            }

            var athleteId = profile.ReferenceAthleteId.Value;

            var diary = new SessionDiary
            {
                SessionId = sessionWithDiary.Session.Id,
                AthleteId = athleteId,
                ValoracionFisica = sessionWithDiary.EditValoracionFisica,
                ValoracionMental = sessionWithDiary.EditValoracionMental,
                ValoracionTecnica = sessionWithDiary.EditValoracionTecnica,
                Notas = sessionWithDiary.EditNotas
            };

            await _databaseService.SaveSessionDiaryAsync(diary);

            // Actualizar el diario en el objeto
            sessionWithDiary.Diary = diary;

            // Recargar los datos del mes para actualizar indicadores del calendario
            await LoadDiaryEntriesForMonthAsync(SelectedDiaryDate, athleteId);

            // Recargar promedios
            await LoadValoracionAveragesAsync(athleteId);
            await LoadValoracionEvolutionAsync();

            await Shell.Current.DisplayAlert("Guardado", "Tu diario de sesión se ha guardado correctamente.", "OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error guardando diario desde calendario: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo guardar el diario: {ex.Message}", "OK");
        }
    }

    private async Task ViewSessionAsPlaylistAsync(SessionWithDiary? sessionWithDiary)
    {
        if (sessionWithDiary == null || !sessionWithDiary.HasVideos) return;

        try
        {
            // Cargar todos los vídeos de la sesión (no solo los 6 de preview)
            var allVideos = await _databaseService.GetVideoClipsBySessionAsync(sessionWithDiary.Session.Id);

            if (allVideos.Count == 0)
            {
                await Shell.Current.DisplayAlert("Sesión", "Esta sesión no tiene vídeos.", "OK");
                return;
            }

            // Ordenar por fecha de creación
            var orderedVideos = allVideos.OrderBy(v => v.CreationDate).ToList();

            // Navegar a SinglePlayerPage con la playlist
            var singlePage = App.Current?.Handler?.MauiContext?.Services.GetService<SinglePlayerPage>();
            if (singlePage?.BindingContext is SinglePlayerViewModel singleVm)
            {
                await singleVm.InitializeWithPlaylistAsync(orderedVideos, 0);
                await Shell.Current.Navigation.PushAsync(singlePage);
            }
            else
            {
                // Fallback: reproducir el primer video
                var firstVideo = orderedVideos.First();
                if (!string.IsNullOrEmpty(firstVideo.LocalClipPath))
                {
                    await Shell.Current.GoToAsync($"{nameof(SinglePlayerPage)}?videoPath={Uri.EscapeDataString(firstVideo.LocalClipPath)}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error abriendo playlist de sesión: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo abrir la sesión: {ex.Message}", "OK");
        }
    }

    private async Task ConnectHealthKitAsync()
    {
        if (!_healthKitService.IsAvailable)
        {
            await Shell.Current.DisplayAlert("No disponible", "La app Salud no está disponible en este dispositivo.", "OK");
            return;
        }

        try
        {
            var authorized = await _healthKitService.RequestAuthorizationAsync();
            IsHealthKitAuthorized = authorized;

            if (authorized)
            {
                await Shell.Current.DisplayAlert("Conectado", "Se han conectado correctamente los datos de la app Salud.", "OK");
                // Recargar datos del día seleccionado
                await LoadHealthDataForDateAsync(SelectedDiaryDate);
            }
            else
            {
                await Shell.Current.DisplayAlert("Permiso denegado",
                    "Para ver tus datos de salud, ve a Ajustes > Privacidad > Salud y permite el acceso a CrownRFEP Reader.", "OK");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error conectando HealthKit: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo conectar con la app Salud: {ex.Message}", "OK");
        }
    }

    private async Task LoadHealthDataForDateAsync(DateTime date)
    {
        if (!_healthKitService.IsAvailable || !IsHealthKitAuthorized)
        {
            SelectedDateHealthData = null;
            return;
        }

        try
        {
            IsLoadingHealthData = true;
            SelectedDateHealthData = await _healthKitService.GetHealthDataForDateAsync(date);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando datos de salud: {ex}");
            SelectedDateHealthData = null;
        }
        finally
        {
            IsLoadingHealthData = false;
        }
    }

    private async Task LoadWellnessDataForDateAsync(DateTime date)
    {
        try
        {
            SelectedDateWellness = await _databaseService.GetDailyWellnessAsync(date);
            if (SelectedDateWellness == null)
            {
                // Crear uno vacío para la fecha seleccionada
                SelectedDateWellness = new DailyWellness { Date = date };
            }
            OnPropertyChanged(nameof(HasWellnessData));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando datos de bienestar: {ex}");
            SelectedDateWellness = new DailyWellness { Date = date };
        }
    }

    private async Task ImportHealthDataToWellnessAsync()
    {
        if (!_healthKitService.IsAvailable)
        {
            await Shell.Current.DisplayAlert("No disponible",
                "La app Salud no está disponible en este dispositivo.", "OK");
            return;
        }

        try
        {
            // Si no está autorizado, solicitar autorización
            if (!IsHealthKitAuthorized)
            {
                var authorized = await _healthKitService.RequestAuthorizationAsync();
                IsHealthKitAuthorized = authorized;

                if (!authorized)
                {
                    await Shell.Current.DisplayAlert("Permiso denegado",
                        "Para importar datos de salud, ve a Ajustes > Privacidad > Salud y permite el acceso a CrownRFEP Reader.", "OK");
                    return;
                }
            }

            // Obtener datos de salud para la fecha seleccionada
            var healthData = await _healthKitService.GetHealthDataForDateAsync(SelectedDiaryDate);

            if (healthData == null || !healthData.HasData)
            {
                await Shell.Current.DisplayAlert("Sin datos",
                    $"No hay datos de salud disponibles para el {SelectedDiaryDate:d MMMM yyyy}.", "OK");
                return;
            }

            // Rellenar los campos del formulario con los datos de HealthKit
            var fieldsUpdated = new List<string>();

            if (healthData.SleepHours.HasValue && healthData.SleepHours > 0)
            {
                WellnessSleepHours = Math.Round(healthData.SleepHours.Value, 1);
                fieldsUpdated.Add($"Sueño: {WellnessSleepHours:F1} h");
            }

            if (healthData.RestingHeartRate.HasValue)
            {
                WellnessRestingHeartRate = healthData.RestingHeartRate.Value;
                fieldsUpdated.Add($"FC reposo: {WellnessRestingHeartRate} bpm");
            }

            if (healthData.HeartRateVariability.HasValue)
            {
                WellnessHRV = (int)Math.Round(healthData.HeartRateVariability.Value);
                fieldsUpdated.Add($"HRV: {WellnessHRV} ms");
            }

            if (fieldsUpdated.Any())
            {
                await Shell.Current.DisplayAlert("Datos importados",
                    $"Se han importado los siguientes datos de la app Salud:\n\n• {string.Join("\n• ", fieldsUpdated)}", "OK");
            }
            else
            {
                await Shell.Current.DisplayAlert("Sin datos útiles",
                    "No se encontraron datos relevantes (sueño, FC, HRV) para importar.", "OK");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error importando datos de salud: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudieron importar los datos: {ex.Message}", "OK");
        }
    }

    private async Task SaveWellnessAsync()
    {
        try
        {
            var wellness = new DailyWellness
            {
                Date = SelectedDiaryDate,
                SleepHours = WellnessSleepHours,
                SleepQuality = WellnessSleepQuality,
                RecoveryFeeling = WellnessRecoveryFeeling,
                MuscleFatigue = WellnessMuscleFatigue,
                MoodRating = WellnessMoodRating,
                RestingHeartRate = WellnessRestingHeartRate,
                HeartRateVariability = WellnessHRV,
                Notes = WellnessNotes
            };

            SelectedDateWellness = await _databaseService.SaveDailyWellnessAsync(wellness);
            IsEditingWellness = false;
            OnPropertyChanged(nameof(HasWellnessData));

            // Actualizar el calendario para reflejar los nuevos datos de bienestar
            var startOfMonth = new DateTime(SelectedDiaryDate.Year, SelectedDiaryDate.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);
            WellnessDataForMonth = await _databaseService.GetDailyWellnessRangeAsync(startOfMonth, endOfMonth);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error guardando bienestar: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo guardar: {ex.Message}", "OK");
        }
    }

    private void CancelEditWellness()
    {
        IsEditingWellness = false;
        // Restaurar valores originales
        if (SelectedDateWellness != null)
        {
            WellnessSleepHours = SelectedDateWellness.SleepHours;
            WellnessSleepQuality = SelectedDateWellness.SleepQuality;
            WellnessRecoveryFeeling = SelectedDateWellness.RecoveryFeeling;
            WellnessMuscleFatigue = SelectedDateWellness.MuscleFatigue;
            WellnessMoodRating = SelectedDateWellness.MoodRating;
            WellnessRestingHeartRate = SelectedDateWellness.RestingHeartRate;
            WellnessHRV = SelectedDateWellness.HeartRateVariability;
            WellnessNotes = SelectedDateWellness.Notes;
        }
        else
        {
            ClearWellnessFields();
        }
    }

    private void ClearWellnessFields()
    {
        WellnessSleepHours = null;
        WellnessSleepQuality = null;
        WellnessRecoveryFeeling = null;
        WellnessMuscleFatigue = null;
        WellnessMoodRating = null;
        WellnessRestingHeartRate = null;
        WellnessHRV = null;
        WellnessNotes = null;
    }

    private async Task ShowPPMInfoAsync()
    {
        await Shell.Current.DisplayAlert(
            "PPM Basal (Frecuencia Cardíaca en Reposo)",
            "La frecuencia cardíaca en reposo (FCR) o PPM basal es el número de latidos por minuto cuando estás completamente relajado.\n\n" +
            "📊 VALORES DE REFERENCIA:\n" +
            "• Atletas de élite: 40-50 bpm\n" +
            "• Muy en forma: 50-60 bpm\n" +
            "• En forma: 60-70 bpm\n" +
            "• Promedio: 70-80 bpm\n" +
            "• Por encima de 80 bpm: mejorable\n\n" +
            "📈 INTERPRETACIÓN:\n" +
            "• Una FCR más baja indica mejor condición cardiovascular\n" +
            "• Si sube 5-10 bpm sobre tu media, puede indicar fatiga, estrés o enfermedad incipiente\n" +
            "• Mídela siempre en las mismas condiciones (al despertar, antes de levantarte)\n\n" +
            "💡 CONSEJO:\n" +
            "Lleva un registro diario para detectar tendencias y ajustar tu entrenamiento.",
            "Entendido");
    }

    private async Task ShowHRVInfoAsync()
    {
        await Shell.Current.DisplayAlert(
            "HRV (Variabilidad de Frecuencia Cardíaca)",
            "La HRV mide la variación en el tiempo entre latidos consecutivos. Se expresa en milisegundos (ms).\n\n" +
            "📊 VALORES DE REFERENCIA:\n" +
            "• Excelente: > 70 ms\n" +
            "• Bueno: 50-70 ms\n" +
            "• Normal: 30-50 ms\n" +
            "• Bajo: < 30 ms\n\n" +
            "📈 INTERPRETACIÓN:\n" +
            "• HRV ALTA = Sistema nervioso equilibrado, buena recuperación, listo para entrenar fuerte\n" +
            "• HRV BAJA = Estrés, fatiga acumulada, necesitas descanso o entrenamiento suave\n\n" +
            "⚠️ IMPORTANTE:\n" +
            "• La HRV es muy individual - compara con TU propia media\n" +
            "• Varía con edad, sexo, genética y estilo de vida\n" +
            "• Una caída del 10-15% respecto a tu media sugiere reducir intensidad\n\n" +
            "💡 CONSEJO:\n" +
            "Mídela cada mañana al despertar con apps como Elite HRV, HRV4Training u Oura Ring.",
            "Entendido");
    }

    private async Task LoadDiaryEntriesForMonthAsync(DateTime month, int athleteId)
    {
        try
        {
            var startOfMonth = new DateTime(month.Year, month.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

            // Cargar sesiones del mes para mostrar iconos de video en el calendario
            var startUnix = new DateTimeOffset(startOfMonth).ToUnixTimeSeconds();
            var endUnix = new DateTimeOffset(endOfMonth.AddDays(1)).ToUnixTimeSeconds();
            var sessions = await _databaseService.GetAllSessionsAsync();
            SessionsForMonth = sessions.Where(s => s.Fecha >= startUnix && s.Fecha < endUnix).ToList();

            // Cargar diarios de las sesiones del mes (filtrados por SessionId)
            var sessionIds = SessionsForMonth.Select(s => s.Id).ToHashSet();
            var allDiaries = await _databaseService.GetAllSessionDiariesForAthleteAsync(athleteId);
            DiaryEntriesForMonth = allDiaries.Where(d => sessionIds.Contains(d.SessionId)).ToList();

            // Cargar datos de bienestar del mes
            WellnessDataForMonth = await _databaseService.GetDailyWellnessRangeAsync(startOfMonth, endOfMonth);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando entradas del mes: {ex}");
            DiaryEntriesForMonth = new List<SessionDiary>();
            SessionsForMonth = new List<Session>();
            WellnessDataForMonth = new List<DailyWellness>();
        }
    }
}
