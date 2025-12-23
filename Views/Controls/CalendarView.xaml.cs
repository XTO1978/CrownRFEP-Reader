using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CrownRFEP_Reader.Models;

namespace CrownRFEP_Reader.Views.Controls;

/// <summary>
/// Modelo de datos para cada día del calendario
/// </summary>
public class CalendarDayModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _hasSession;
    private bool _hasDiary;
    private bool _hasValoraciones;
    private bool _hasWellness;
    private int? _wellnessScore;
    private string? _moodSymbol;
    private Color? _moodColor;
    private double? _sleepHours;
    private int? _restingHeartRate;
    private int? _hrv;

    public DateTime Date { get; set; }
    public string DayNumber => Date.Day.ToString();
    public bool IsVisible { get; set; } = true;
    public bool IsToday => Date.Date == DateTime.Today;
    public bool IsFuture => Date.Date > DateTime.Today;
    public bool IsEnabled => !IsFuture;

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); UpdateColors(); }
    }

    public bool HasSession
    {
        get => _hasSession;
        set { _hasSession = value; OnPropertyChanged(); }
    }

    public bool HasDiary
    {
        get => _hasDiary;
        set { _hasDiary = value; OnPropertyChanged(); }
    }

    public bool HasValoraciones
    {
        get => _hasValoraciones;
        set { _hasValoraciones = value; OnPropertyChanged(); }
    }

    public bool HasWellness
    {
        get => _hasWellness;
        set { _hasWellness = value; OnPropertyChanged(); }
    }

    public int? WellnessScore
    {
        get => _wellnessScore;
        set { _wellnessScore = value; OnPropertyChanged(); OnPropertyChanged(nameof(WellnessScoreText)); }
    }

    public string WellnessScoreText => WellnessScore.HasValue ? WellnessScore.Value.ToString() : "";

    public string? MoodSymbol
    {
        get => _moodSymbol;
        set { _moodSymbol = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMood)); }
    }

    public bool HasMood => !string.IsNullOrEmpty(MoodSymbol);

    public Color? MoodColor
    {
        get => _moodColor;
        set { _moodColor = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayMoodColor)); }
    }

    public Color DisplayMoodColor => IsSelected ? Colors.Black : (MoodColor ?? Color.FromArgb("#FF888888"));

    public double? SleepHours
    {
        get => _sleepHours;
        set { _sleepHours = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSleep)); OnPropertyChanged(nameof(SleepText)); }
    }

    public bool HasSleep => SleepHours.HasValue && SleepHours > 0;

    public string SleepText => SleepHours.HasValue ? $"{SleepHours:F0}h" : "";

    public int? RestingHeartRate
    {
        get => _restingHeartRate;
        set { _restingHeartRate = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasHeartRate)); OnPropertyChanged(nameof(HeartRateText)); }
    }

    public bool HasHeartRate => RestingHeartRate.HasValue && RestingHeartRate > 0;

    public string HeartRateText => RestingHeartRate.HasValue ? RestingHeartRate.Value.ToString() : "";

    public int? HRV
    {
        get => _hrv;
        set { _hrv = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasHRV)); OnPropertyChanged(nameof(HRVText)); }
    }

    public bool HasHRV => HRV.HasValue && HRV > 0;

    public string HRVText => HRV.HasValue ? HRV.Value.ToString() : "";

    // Opacidad para días futuros (desactivados)
    public double DayOpacity => IsFuture ? 0.3 : 1.0;

    // Colores calculados
    public Color BackgroundColor => IsFuture ? Colors.Transparent :
                                    (IsSelected ? Color.FromArgb("#FF6DDDFF") :
                                    (IsToday ? Color.FromArgb("#FF3A3A3A") : Colors.Transparent));

    public Color StrokeColor => IsFuture ? Color.FromArgb("#FF1A1A1A") :
                                (IsSelected ? Colors.Transparent :
                                (IsToday ? Color.FromArgb("#FF6DDDFF") : Color.FromArgb("#FF2A2A2A")));

    public double StrokeThickness => IsToday && !IsSelected ? 2 : 1;

    public Color TextColor => IsFuture ? Color.FromArgb("#FF444444") :
                              (IsSelected ? Colors.Black : Colors.White);

    public Color VideoIconColor => IsSelected ? Colors.Black : Color.FromArgb("#FF6DDDFF");

    public Color DiaryIconColor => IsSelected ? Colors.Black : Color.FromArgb("#FF8DD3C7");

    public Color ValoracionesIconColor => IsSelected ? Colors.Black : Color.FromArgb("#FFFFED6F");

    public Color WellnessIconColor => IsSelected ? Colors.Black : Color.FromArgb("#FFFF6B6B");

    public Color SleepIconColor => IsSelected ? Colors.Black : Color.FromArgb("#FF5856D6");

    public Color HeartRateIconColor => IsSelected ? Colors.Black : Color.FromArgb("#FFFF6B6B");

    public Color HRVIconColor => IsSelected ? Colors.Black : Color.FromArgb("#FFAEADFF");

    private void UpdateColors()
    {
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(StrokeColor));
        OnPropertyChanged(nameof(StrokeThickness));
        OnPropertyChanged(nameof(TextColor));
        OnPropertyChanged(nameof(VideoIconColor));
        OnPropertyChanged(nameof(DiaryIconColor));
        OnPropertyChanged(nameof(ValoracionesIconColor));
        OnPropertyChanged(nameof(WellnessIconColor));
        OnPropertyChanged(nameof(SleepIconColor));
        OnPropertyChanged(nameof(HeartRateIconColor));
        OnPropertyChanged(nameof(HRVIconColor));
        OnPropertyChanged(nameof(DisplayMoodColor));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Control de calendario personalizado para mostrar fechas con indicadores de diario y sesiones
/// </summary>
public partial class CalendarView : ContentView, INotifyPropertyChanged
{
    private DateTime _displayMonth;
    private string _displayMonthText = "";

    #region Bindable Properties

    public static readonly BindableProperty SelectedDateProperty =
        BindableProperty.Create(nameof(SelectedDate), typeof(DateTime), typeof(CalendarView), DateTime.Today,
            propertyChanged: OnSelectedDateChanged);

    public static readonly BindableProperty DateSelectedCommandProperty =
        BindableProperty.Create(nameof(DateSelectedCommand), typeof(ICommand), typeof(CalendarView));

    public static readonly BindableProperty DiaryEntriesProperty =
        BindableProperty.Create(nameof(DiaryEntries), typeof(IList<SessionDiary>), typeof(CalendarView),
            propertyChanged: OnDataChanged);

    public static readonly BindableProperty SessionsProperty =
        BindableProperty.Create(nameof(Sessions), typeof(IList<Session>), typeof(CalendarView),
            propertyChanged: OnDataChanged);

    public static readonly BindableProperty WellnessDataProperty =
        BindableProperty.Create(nameof(WellnessData), typeof(IList<DailyWellness>), typeof(CalendarView),
            propertyChanged: OnDataChanged);

    public DateTime SelectedDate
    {
        get => (DateTime)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public ICommand DateSelectedCommand
    {
        get => (ICommand)GetValue(DateSelectedCommandProperty);
        set => SetValue(DateSelectedCommandProperty, value);
    }

    public IList<SessionDiary> DiaryEntries
    {
        get => (IList<SessionDiary>)GetValue(DiaryEntriesProperty);
        set => SetValue(DiaryEntriesProperty, value);
    }

    public IList<Session> Sessions
    {
        get => (IList<Session>)GetValue(SessionsProperty);
        set => SetValue(SessionsProperty, value);
    }

    public IList<DailyWellness> WellnessData
    {
        get => (IList<DailyWellness>)GetValue(WellnessDataProperty);
        set => SetValue(WellnessDataProperty, value);
    }

    #endregion

    #region Public Properties for XAML Binding

    public ObservableCollection<CalendarDayModel> CalendarDays { get; } = new();

    public string DisplayMonthText
    {
        get => _displayMonthText;
        private set
        {
            if (_displayMonthText != value)
            {
                _displayMonthText = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand TapCommand { get; }

    #endregion

    public CalendarView()
    {
        TapCommand = new Command<DateTime>(OnDateTapped);
        InitializeComponent();
        _displayMonth = DateTime.Today;
        UpdateMonthLabel();
        PopulateDays();
    }

    #region Property Changed Handlers

    private static void OnSelectedDateChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CalendarView calendar && newValue is DateTime date)
        {
            if (calendar._displayMonth.Year != date.Year || calendar._displayMonth.Month != date.Month)
            {
                calendar._displayMonth = new DateTime(date.Year, date.Month, 1);
                calendar.UpdateMonthLabel();
                calendar.PopulateDays();
            }
            else
            {
                // Solo actualizar la selección sin repoblar
                calendar.UpdateSelection();
            }
        }
    }

    private static void OnDataChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CalendarView calendar)
        {
            calendar.UpdateDayIndicators();
        }
    }

    #endregion

    #region Event Handlers

    private void OnPreviousMonthClicked(object? sender, EventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(-1);
        UpdateMonthLabel();
        PopulateDays();
    }

    private void OnNextMonthClicked(object? sender, EventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(1);
        UpdateMonthLabel();
        PopulateDays();
    }

    private void OnDateTapped(DateTime date)
    {
        // No permitir seleccionar días futuros
        if (date.Date > DateTime.Today)
            return;
            
        SelectedDate = date;
        DateSelectedCommand?.Execute(date);
    }

    #endregion

    #region Private Methods

    private void UpdateMonthLabel()
    {
        DisplayMonthText = _displayMonth.ToString("MMMM yyyy", new System.Globalization.CultureInfo("es-ES"));
    }

    private void PopulateDays()
    {
        CalendarDays.Clear();

        var firstDayOfMonth = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month);

        // Lunes = 0, Domingo = 6
        int startDayOfWeek = ((int)firstDayOfMonth.DayOfWeek + 6) % 7;

        // Añadir espacios vacíos para los días anteriores al primer día del mes
        for (int i = 0; i < startDayOfWeek; i++)
        {
            CalendarDays.Add(new CalendarDayModel
            {
                Date = DateTime.MinValue,
                IsVisible = false
            });
        }

        // Añadir los días del mes
        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(_displayMonth.Year, _displayMonth.Month, day);
            var wellness = GetWellnessForDate(date);
            var dayModel = new CalendarDayModel
            {
                Date = date,
                IsSelected = date.Date == SelectedDate.Date,
                HasSession = HasSessionOnDate(date),
                HasDiary = HasDiaryEntry(date),
                HasValoraciones = HasValoracionesOnDate(date),
                HasWellness = wellness != null && wellness.HasData,
                WellnessScore = wellness?.WellnessScore,
                MoodSymbol = wellness?.MoodSymbol,
                MoodColor = wellness?.MoodColor,
                SleepHours = wellness?.SleepHours,
                RestingHeartRate = wellness?.RestingHeartRate,
                HRV = wellness?.HeartRateVariability
            };
            CalendarDays.Add(dayModel);
        }
    }

    private void UpdateSelection()
    {
        foreach (var day in CalendarDays)
        {
            if (day.IsVisible)
            {
                day.IsSelected = day.Date.Date == SelectedDate.Date;
            }
        }
    }

    private void UpdateDayIndicators()
    {
        foreach (var day in CalendarDays)
        {
            if (day.IsVisible && day.Date != DateTime.MinValue)
            {
                day.HasSession = HasSessionOnDate(day.Date);
                day.HasDiary = HasDiaryEntry(day.Date);
                day.HasValoraciones = HasValoracionesOnDate(day.Date);
                
                var wellness = GetWellnessForDate(day.Date);
                day.HasWellness = wellness != null && wellness.HasData;
                day.WellnessScore = wellness?.WellnessScore;
                day.MoodSymbol = wellness?.MoodSymbol;
                day.MoodColor = wellness?.MoodColor;
                day.SleepHours = wellness?.SleepHours;
                day.RestingHeartRate = wellness?.RestingHeartRate;
                day.HRV = wellness?.HeartRateVariability;
            }
        }
    }

    private bool HasDiaryEntry(DateTime date)
    {
        if (DiaryEntries == null || DiaryEntries.Count == 0 || Sessions == null || Sessions.Count == 0)
            return false;

        // Un diario está asociado a una sesión. Mostramos el icono de diario en la fecha de la sesión.
        foreach (var diary in DiaryEntries)
        {
            var session = Sessions.FirstOrDefault(s => s.Id == diary.SessionId);
            if (session != null && session.FechaDateTime.Date == date.Date)
                return true;
        }
        return false;
    }

    private bool HasSessionOnDate(DateTime date)
    {
        if (Sessions == null || Sessions.Count == 0)
            return false;

        return Sessions.Any(s => s.FechaDateTime.Date == date.Date);
    }

    private bool HasValoracionesOnDate(DateTime date)
    {
        if (DiaryEntries == null || DiaryEntries.Count == 0 || Sessions == null || Sessions.Count == 0)
            return false;

        // Buscar si hay valoraciones para alguna sesión de este día
        foreach (var diary in DiaryEntries)
        {
            if (diary.ValoracionFisica > 0 || diary.ValoracionMental > 0 || diary.ValoracionTecnica > 0)
            {
                var session = Sessions.FirstOrDefault(s => s.Id == diary.SessionId);
                if (session != null && session.FechaDateTime.Date == date.Date)
                    return true;
            }
        }
        return false;
    }

    private DailyWellness? GetWellnessForDate(DateTime date)
    {
        if (WellnessData == null || WellnessData.Count == 0)
            return null;

        return WellnessData.FirstOrDefault(w => w.Date.Date == date.Date);
    }

    #endregion

    #region INotifyPropertyChanged

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected new void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    #endregion
}
