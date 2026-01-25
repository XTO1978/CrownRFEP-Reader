using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using Microsoft.Maui.Storage;

namespace CrownRFEP_Reader.ViewModels;

public class SmartFoldersViewModel : ObservableObject
{
    private const string SmartFoldersPreferencesKey = "SmartFolders";

    private Func<List<VideoClip>?> _getAllVideosCache = () => null;
    private Func<Task> _loadAllVideosAsync = () => Task.CompletedTask;
    private Action _clearFiltersSkipApply = () => { };
    private Action _clearSelectedSession = () => { };
    private Action _setAllGallerySelected = () => { };
    private Func<List<VideoClip>, Task> _applyFilteredResultsAsync = _ => Task.CompletedTask;
    private Func<int> _incrementFiltersVersion = () => 0;
    private Func<int> _getFiltersVersion = () => 0;
    private Action<bool> _setSmartFolderSidebarPopupVisible = _ => { };
    private Action<SmartFolderDefinition> _openIconColorPickerForSmartFolder = _ => { };

    private string _newSmartFolderName = string.Empty;
    private string _newSmartFolderMatchMode = "All";
    private int _newSmartFolderLiveMatchCount;
    private bool _isSmartFoldersExpanded = true;
    private SmartFolderDefinition? _activeSmartFolder;
    private List<VideoClip>? _smartFolderFilteredVideosCache;

    public SmartFoldersViewModel()
    {
        OpenSmartFolderSidebarPopupCommand = new RelayCommand(OpenSmartFolderSidebarPopup);
        CancelSmartFolderSidebarPopupCommand = new RelayCommand(CloseSmartFolderSidebarPopup);
        AddSmartFolderCriterionCommand = new RelayCommand(AddSmartFolderCriterion);
        RemoveSmartFolderCriterionCommand = new RelayCommand<SmartFolderCriterion>(RemoveSmartFolderCriterion);
        CreateSmartFolderCommand = new RelayCommand(CreateSmartFolder);
        SetSmartFolderMatchModeCommand = new RelayCommand<string>(SetSmartFolderMatchMode);
        SelectSmartFolderCommand = new AsyncRelayCommand<SmartFolderDefinition>(SelectSmartFolderAsync);
        SelectCriterionFieldCommand = new AsyncRelayCommand<SmartFolderCriterion>(SelectCriterionFieldAsync);
        SelectCriterionOperatorCommand = new AsyncRelayCommand<SmartFolderCriterion>(SelectCriterionOperatorAsync);

        ShowSmartFolderContextMenuCommand = new AsyncRelayCommand<SmartFolderDefinition>(ShowSmartFolderContextMenuAsync);
        RenameSmartFolderCommand = new AsyncRelayCommand<SmartFolderDefinition>(RenameSmartFolderAsync);
        DeleteSmartFolderCommand = new AsyncRelayCommand<SmartFolderDefinition>(DeleteSmartFolderAsync);
        ChangeSmartFolderIconCommand = new AsyncRelayCommand<SmartFolderDefinition>(ChangeSmartFolderIconAsync);
        ChangeSmartFolderColorCommand = new AsyncRelayCommand<SmartFolderDefinition>(ChangeSmartFolderColorAsync);
        SetSmartFolderIconCommand = new RelayCommand<object?>(SetSmartFolderIcon);
        SetSmartFolderColorCommand = new RelayCommand<object?>(SetSmartFolderColor);
        OpenIconColorPickerForSmartFolderCommand = new RelayCommand<SmartFolderDefinition>(OpenIconColorPickerForSmartFolder);

        ToggleSmartFoldersExpansionCommand = new RelayCommand(ToggleSmartFoldersExpansion);

        LoadSmartFoldersFromPreferences();
    }

    public void Configure(
        Func<List<VideoClip>?> getAllVideosCache,
        Func<Task> loadAllVideosAsync,
        Action clearFiltersSkipApply,
        Action clearSelectedSession,
        Action setAllGallerySelected,
        Func<List<VideoClip>, Task> applyFilteredResultsAsync,
        Func<int> incrementFiltersVersion,
        Func<int> getFiltersVersion,
        Action<bool> setSmartFolderSidebarPopupVisible,
        Action<SmartFolderDefinition> openIconColorPickerForSmartFolder)
    {
        _getAllVideosCache = getAllVideosCache;
        _loadAllVideosAsync = loadAllVideosAsync;
        _clearFiltersSkipApply = clearFiltersSkipApply;
        _clearSelectedSession = clearSelectedSession;
        _setAllGallerySelected = setAllGallerySelected;
        _applyFilteredResultsAsync = applyFilteredResultsAsync;
        _incrementFiltersVersion = incrementFiltersVersion;
        _getFiltersVersion = getFiltersVersion;
        _setSmartFolderSidebarPopupVisible = setSmartFolderSidebarPopupVisible;
        _openIconColorPickerForSmartFolder = openIconColorPickerForSmartFolder;
    }

    public ObservableCollection<SmartFolderDefinition> SmartFolders { get; } = new();
    public ObservableCollection<SmartFolderCriterion> NewSmartFolderCriteria { get; } = new();

    public ObservableCollection<string> SmartFolderFieldOptions { get; } = new()
    {
        "Lugar",
        "Deportista",
        "Sección",
        "Tag",
        "Fecha",
    };

    public SmartFolderDefinition? ActiveSmartFolder => _activeSmartFolder;

    public List<VideoClip>? SmartFolderFilteredVideosCache => _smartFolderFilteredVideosCache;

    public bool IsSmartFoldersExpanded
    {
        get => _isSmartFoldersExpanded;
        set => SetProperty(ref _isSmartFoldersExpanded, value);
    }

    public string NewSmartFolderName
    {
        get => _newSmartFolderName;
        set => SetProperty(ref _newSmartFolderName, value);
    }

    public string NewSmartFolderMatchMode
    {
        get => _newSmartFolderMatchMode;
        set
        {
            if (SetProperty(ref _newSmartFolderMatchMode, value))
            {
                OnPropertyChanged(nameof(IsSmartFolderMatchAll));
                OnPropertyChanged(nameof(IsSmartFolderMatchAny));
                RecomputeNewSmartFolderLiveMatchCount();
            }
        }
    }

    public bool IsSmartFolderMatchAll
    {
        get => NewSmartFolderMatchMode == "All";
        set
        {
            if (value)
                NewSmartFolderMatchMode = "All";
        }
    }

    public bool IsSmartFolderMatchAny
    {
        get => NewSmartFolderMatchMode == "Any";
        set
        {
            if (value)
                NewSmartFolderMatchMode = "Any";
        }
    }

    public int NewSmartFolderLiveMatchCount
    {
        get => _newSmartFolderLiveMatchCount;
        private set => SetProperty(ref _newSmartFolderLiveMatchCount, value);
    }

    public ICommand OpenSmartFolderSidebarPopupCommand { get; }
    public ICommand CancelSmartFolderSidebarPopupCommand { get; }
    public ICommand AddSmartFolderCriterionCommand { get; }
    public ICommand RemoveSmartFolderCriterionCommand { get; }
    public ICommand CreateSmartFolderCommand { get; }
    public ICommand SetSmartFolderMatchModeCommand { get; }
    public ICommand SelectSmartFolderCommand { get; }
    public ICommand SelectCriterionFieldCommand { get; }
    public ICommand SelectCriterionOperatorCommand { get; }
    public ICommand ShowSmartFolderContextMenuCommand { get; }
    public ICommand RenameSmartFolderCommand { get; }
    public ICommand DeleteSmartFolderCommand { get; }
    public ICommand ChangeSmartFolderIconCommand { get; }
    public ICommand ChangeSmartFolderColorCommand { get; }
    public ICommand SetSmartFolderIconCommand { get; }
    public ICommand SetSmartFolderColorCommand { get; }
    public ICommand OpenIconColorPickerForSmartFolderCommand { get; }
    public ICommand ToggleSmartFoldersExpansionCommand { get; }

    public void ClearActiveSmartFolder()
    {
        _activeSmartFolder = null;
        _smartFolderFilteredVideosCache = null;
    }

    public void UpdateSmartFolderVideoCounts()
    {
        var source = _getAllVideosCache();
        if (source == null)
            return;

        foreach (var folder in SmartFolders)
        {
            var criteria = folder.Criteria ?? new List<SmartFolderCriterion>();
            bool matchAll = !string.Equals(folder.MatchMode, "Any", StringComparison.OrdinalIgnoreCase);

            int count;
            if (criteria.Count == 0)
            {
                count = source.Count;
            }
            else
            {
                count = source.Count(v =>
                {
                    var matches = criteria.Select(c => MatchesCriterion(v, c)).ToList();
                    return matchAll ? matches.All(m => m) : matches.Any(m => m);
                });
            }

            folder.MatchingVideoCount = count;
        }
    }

    private void OpenSmartFolderSidebarPopup()
    {
        NewSmartFolderName = string.Empty;
        NewSmartFolderMatchMode = "All";

        foreach (var existing in NewSmartFolderCriteria)
            existing.PropertyChanged -= OnNewSmartFolderCriterionChanged;

        NewSmartFolderCriteria.Clear();
        AddSmartFolderCriterion();
        RecomputeNewSmartFolderLiveMatchCount();

        _setSmartFolderSidebarPopupVisible(true);
    }

    private void SetSmartFolderMatchMode(string? matchMode)
    {
        if (string.IsNullOrWhiteSpace(matchMode))
            return;

        if (string.Equals(matchMode, "All", StringComparison.OrdinalIgnoreCase))
            NewSmartFolderMatchMode = "All";
        else if (string.Equals(matchMode, "Any", StringComparison.OrdinalIgnoreCase))
            NewSmartFolderMatchMode = "Any";
    }

    private void CloseSmartFolderSidebarPopup()
    {
        _setSmartFolderSidebarPopupVisible(false);
    }

    private void AddSmartFolderCriterion()
    {
        var criterion = new SmartFolderCriterion();
        criterion.PropertyChanged += OnNewSmartFolderCriterionChanged;
        NewSmartFolderCriteria.Add(criterion);
        RecomputeNewSmartFolderLiveMatchCount();
    }

    private void RemoveSmartFolderCriterion(SmartFolderCriterion? criterion)
    {
        if (criterion == null)
            return;

        criterion.PropertyChanged -= OnNewSmartFolderCriterionChanged;
        NewSmartFolderCriteria.Remove(criterion);
        RecomputeNewSmartFolderLiveMatchCount();
    }

    private void OnNewSmartFolderCriterionChanged(object? sender, PropertyChangedEventArgs e)
    {
        RecomputeNewSmartFolderLiveMatchCount();
    }

    private async Task SelectCriterionFieldAsync(SmartFolderCriterion? criterion)
    {
        if (criterion == null)
            return;

        try
        {
            var page = Application.Current?.Windows?.FirstOrDefault()?.Page;
            if (page == null)
                return;

            var options = SmartFolderFieldOptions.ToArray();
            var result = await page.DisplayActionSheet("Seleccionar campo", "Cancelar", null, options);
            if (!string.IsNullOrEmpty(result) && result != "Cancelar")
            {
                criterion.Field = result;
            }
        }
        catch { }
    }

    private async Task SelectCriterionOperatorAsync(SmartFolderCriterion? criterion)
    {
        if (criterion == null)
            return;

        try
        {
            var page = Application.Current?.Windows?.FirstOrDefault()?.Page;
            if (page == null)
                return;

            var options = criterion.AvailableOperators.ToArray();
            var result = await page.DisplayActionSheet("Seleccionar operador", "Cancelar", null, options);
            if (!string.IsNullOrEmpty(result) && result != "Cancelar")
            {
                criterion.Operator = result;
            }
        }
        catch { }
    }

    private void CreateSmartFolder()
    {
        var name = (NewSmartFolderName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        var definition = new SmartFolderDefinition
        {
            Name = name,
            MatchMode = NewSmartFolderMatchMode == "Any" ? "Any" : "All",
            MatchingVideoCount = NewSmartFolderLiveMatchCount,
            Criteria = NewSmartFolderCriteria
                .Select(c => new SmartFolderCriterion
                {
                    Field = c.Field,
                    Operator = c.Operator,
                    Value = c.Value,
                    Value2 = c.Value2,
                })
                .ToList(),
        };

        SmartFolders.Add(definition);

        UpdateSmartFolderVideoCounts();
        SaveSmartFoldersToPreferences();
        CloseSmartFolderSidebarPopup();
    }

    private void ToggleSmartFoldersExpansion()
    {
        IsSmartFoldersExpanded = !IsSmartFoldersExpanded;
    }

    private async Task SelectSmartFolderAsync(SmartFolderDefinition? definition)
    {
        if (definition == null)
            return;

        _clearSelectedSession();
        _setAllGallerySelected();

        if (_getAllVideosCache() == null)
            await _loadAllVideosAsync();

        await ApplySmartFolderFilterAsync(definition);
    }

    private async Task ApplySmartFolderFilterAsync(SmartFolderDefinition definition)
    {
        var version = _incrementFiltersVersion();

        var source = _getAllVideosCache();
        if (source == null)
            return;

        _activeSmartFolder = definition;

        _clearFiltersSkipApply();

        var criteria = definition.Criteria ?? new List<SmartFolderCriterion>();
        bool matchAll = !string.Equals(definition.MatchMode, "Any", StringComparison.OrdinalIgnoreCase);

        List<VideoClip> filtered;
        if (criteria.Count == 0)
        {
            filtered = source.ToList();
        }
        else
        {
            filtered = source
                .Where(v =>
                {
                    var matches = criteria.Select(c => MatchesCriterion(v, c)).ToList();
                    return matchAll ? matches.All(m => m) : matches.Any(m => m);
                })
                .ToList();
        }

        _smartFolderFilteredVideosCache = filtered;

        if (version != _getFiltersVersion())
            return;

        await _applyFilteredResultsAsync(filtered);
    }

    public void LoadSmartFoldersFromPreferences()
    {
        try
        {
            var json = Preferences.Get(SmartFoldersPreferencesKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var parsed = JsonSerializer.Deserialize<List<SmartFolderDefinition>>(json);
            if (parsed == null)
                return;

            SmartFolders.Clear();
            foreach (var def in parsed)
                SmartFolders.Add(def);
        }
        catch
        {
            // Si falla la deserialización, ignorar y seguir sin carpetas.
        }
    }

    public void SaveSmartFoldersToPreferences()
    {
        try
        {
            var json = JsonSerializer.Serialize(SmartFolders.ToList());
            Preferences.Set(SmartFoldersPreferencesKey, json);
        }
        catch
        {
            // Ignorar errores de persistencia.
        }
    }

    #region Smart Folder Context Menu

    private static readonly string[] SmartFolderIconOptions = new[]
    {
        "folder", "folder.fill", "star", "star.fill", "heart", "heart.fill",
        "flag", "flag.fill", "bookmark", "bookmark.fill", "tag", "tag.fill",
        "bolt", "bolt.fill", "flame", "flame.fill", "trophy", "trophy.fill",
        "sportscourt", "figure.run"
    };

    private static readonly (string Name, string HexColor)[] SmartFolderColorOptions = new[]
    {
        ("Gris", "#FF888888"),
        ("Rojo", "#FFFF453A"),
        ("Naranja", "#FFFF9F0A"),
        ("Amarillo", "#FFFFD60A"),
        ("Verde", "#FF30D158"),
        ("Menta", "#FF63E6E2"),
        ("Cyan", "#FF64D2FF"),
        ("Azul", "#FF0A84FF"),
        ("Morado", "#FFBF5AF2"),
        ("Rosa", "#FFFF375F")
    };

    private async Task ShowSmartFolderContextMenuAsync(SmartFolderDefinition? folder)
    {
        if (folder == null) return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return;

        var result = await page.DisplayActionSheet(
            folder.Name,
            "Cancelar",
            null,
            "Renombrar",
            "Cambiar icono",
            "Cambiar color",
            "Eliminar");

        switch (result)
        {
            case "Renombrar":
                await RenameSmartFolderAsync(folder);
                break;
            case "Cambiar icono":
                await ChangeSmartFolderIconAsync(folder);
                break;
            case "Cambiar color":
                await ChangeSmartFolderColorAsync(folder);
                break;
            case "Eliminar":
                await DeleteSmartFolderAsync(folder);
                break;
        }
    }

    private async Task RenameSmartFolderAsync(SmartFolderDefinition? folder)
    {
        if (folder == null) return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return;

        var newName = await page.DisplayPromptAsync(
            "Renombrar carpeta",
            "Introduce el nuevo nombre:",
            "Aceptar",
            "Cancelar",
            folder.Name,
            50,
            Keyboard.Text,
            folder.Name);

        if (!string.IsNullOrWhiteSpace(newName) && newName != folder.Name)
        {
            folder.Name = newName;
            SaveSmartFoldersToPreferences();
        }
    }

    private async Task DeleteSmartFolderAsync(SmartFolderDefinition? folder)
    {
        if (folder == null) return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return;

        var confirm = await page.DisplayAlert(
            "Eliminar carpeta",
            $"¿Seguro que quieres eliminar la carpeta \"{folder.Name}\"?",
            "Eliminar",
            "Cancelar");

        if (confirm)
        {
            SmartFolders.Remove(folder);
            SaveSmartFoldersToPreferences();
        }
    }

    private async Task ChangeSmartFolderIconAsync(SmartFolderDefinition? folder)
    {
        if (folder == null) return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return;

        var result = await page.DisplayActionSheet(
            "Seleccionar icono",
            "Cancelar",
            null,
            SmartFolderIconOptions);

        if (!string.IsNullOrEmpty(result) && result != "Cancelar")
        {
            folder.Icon = result;
            SaveSmartFoldersToPreferences();
        }
    }

    private async Task ChangeSmartFolderColorAsync(SmartFolderDefinition? folder)
    {
        if (folder == null) return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return;

        var colorNames = SmartFolderColorOptions.Select(c => c.Name).ToArray();
        var result = await page.DisplayActionSheet(
            "Seleccionar color",
            "Cancelar",
            null,
            colorNames);

        if (!string.IsNullOrEmpty(result) && result != "Cancelar")
        {
            var selected = SmartFolderColorOptions.FirstOrDefault(c => c.Name == result);
            if (!string.IsNullOrEmpty(selected.HexColor))
            {
                folder.IconColor = selected.HexColor;
                SaveSmartFoldersToPreferences();
            }
        }
    }

    private void SetSmartFolderIcon(object? parameter)
    {
        if (parameter is ValueTuple<SmartFolderDefinition, string> tuple)
        {
            var (folder, icon) = tuple;
            folder.Icon = icon;
            SaveSmartFoldersToPreferences();
        }
    }

    private void SetSmartFolderColor(object? parameter)
    {
        if (parameter is ValueTuple<SmartFolderDefinition, string> tuple)
        {
            var (folder, color) = tuple;
            folder.IconColor = color;
            SaveSmartFoldersToPreferences();
        }
    }

    private void OpenIconColorPickerForSmartFolder(SmartFolderDefinition? folder)
    {
        if (folder == null) return;
        _openIconColorPickerForSmartFolder(folder);
    }

    #endregion

    private void RecomputeNewSmartFolderLiveMatchCount()
    {
        try
        {
            var source = _getAllVideosCache();
            if (source == null || source.Count == 0)
            {
                NewSmartFolderLiveMatchCount = 0;
                return;
            }

            var criteriaSnapshot = NewSmartFolderCriteria.ToList();
            if (criteriaSnapshot.Count == 0)
            {
                NewSmartFolderLiveMatchCount = source.Count;
                return;
            }

            bool matchAll = NewSmartFolderMatchMode != "Any";

            int count = 0;
            foreach (var video in source)
            {
                var matches = criteriaSnapshot.Select(c => MatchesCriterion(video, c)).ToList();
                var ok = matchAll ? matches.All(m => m) : matches.Any(m => m);
                if (ok)
                    count++;
            }

            NewSmartFolderLiveMatchCount = count;
        }
        catch
        {
            NewSmartFolderLiveMatchCount = 0;
        }
    }

    private static bool MatchesCriterion(VideoClip video, SmartFolderCriterion c)
    {
        var field = (c.Field ?? string.Empty).Trim();
        var op = (c.Operator ?? string.Empty).Trim();
        var value = (c.Value ?? string.Empty).Trim();
        var value2 = (c.Value2 ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(field))
            return true;

        if (field == "Fecha")
        {
            var dt = video.CreationDateTime.Date;

            static bool TryParseDate(string s, out DateTime d)
            {
                if (DateTime.TryParse(s, CultureInfo.GetCultureInfo("es-ES"), DateTimeStyles.AssumeLocal, out d))
                {
                    d = d.Date;
                    return true;
                }
                return false;
            }

            if (op == "Entre")
            {
                if (!TryParseDate(value, out var from) || !TryParseDate(value2, out var to))
                    return false;
                if (to < from)
                    (from, to) = (to, from);
                return dt >= from && dt <= to;
            }

            if (op == "Hasta")
            {
                if (!TryParseDate(value, out var to))
                    return false;
                return dt <= to;
            }

            if (!TryParseDate(value, out var fromDate))
                return false;
            return dt >= fromDate;
        }

        if (field == "Sección")
        {
            if (!int.TryParse(value, out var section))
                return false;
            return video.Section == section;
        }

        string haystack = field switch
        {
            "Lugar" => video.Session?.Lugar ?? string.Empty,
            "Deportista" => video.Atleta?.NombreCompleto ?? string.Empty,
            "Tag" => string.Join(" ", (video.Tags ?? new List<Tag>()).Select(t => t.NombreTag ?? string.Empty))
                + " " + string.Join(" ", (video.EventTags ?? new List<Tag>()).Select(t => t.NombreTag ?? string.Empty)),
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (op == "Es")
            return string.Equals(haystack.Trim(), value, StringComparison.OrdinalIgnoreCase);

        return haystack.Contains(value, StringComparison.OrdinalIgnoreCase);
    }
}
