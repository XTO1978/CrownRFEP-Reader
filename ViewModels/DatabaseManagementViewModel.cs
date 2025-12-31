using System.Collections.ObjectModel;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// ViewModel para la gestión de base de datos.
/// Permite fusionar entidades duplicadas: Atletas, Lugares, Categorías, Tags y Tipos de Sesión.
/// </summary>
public class DatabaseManagementViewModel : BaseViewModel
{
    private readonly DatabaseService _databaseService;
    private int _selectedTabIndex;
    
    // Contadores de selección por entidad
    private int _selectedAthletesCount;
    private int _selectedPlacesCount;
    private int _selectedCategoriesCount;
    private int _selectedTagsCount;
    private int _selectedEventsCount;

    // Colecciones
    public ObservableCollection<Athlete> Athletes { get; } = new();
    public ObservableCollection<Place> Places { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<Tag> Tags { get; } = new();
    public ObservableCollection<EventTagDefinition> Events { get; } = new();

    #region Properties

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, value))
            {
                OnPropertyChanged(nameof(IsAthletesTab));
                OnPropertyChanged(nameof(IsPlacesTab));
                OnPropertyChanged(nameof(IsCategoriesTab));
                OnPropertyChanged(nameof(IsTagsTab));
                OnPropertyChanged(nameof(IsEventsTab));
                OnPropertyChanged(nameof(CurrentTabTitle));
                OnPropertyChanged(nameof(CanMergeCurrentTab));
                OnPropertyChanged(nameof(CurrentSelectedCount));
            }
        }
    }

    public bool IsAthletesTab => SelectedTabIndex == 0;
    public bool IsPlacesTab => SelectedTabIndex == 1;
    public bool IsCategoriesTab => SelectedTabIndex == 2;
    public bool IsTagsTab => SelectedTabIndex == 3;
    public bool IsEventsTab => SelectedTabIndex == 4;

    public string CurrentTabTitle => SelectedTabIndex switch
    {
        0 => "Atletas",
        1 => "Lugares",
        2 => "Categorías",
        3 => "Tags",
        4 => "Eventos",
        _ => ""
    };

    public int SelectedAthletesCount
    {
        get => _selectedAthletesCount;
        set { SetProperty(ref _selectedAthletesCount, value); OnPropertyChanged(nameof(CanMergeCurrentTab)); OnPropertyChanged(nameof(CanDeleteCurrentTab)); OnPropertyChanged(nameof(CanEditCurrentTab)); OnPropertyChanged(nameof(CurrentSelectedCount)); }
    }

    public int SelectedPlacesCount
    {
        get => _selectedPlacesCount;
        set { SetProperty(ref _selectedPlacesCount, value); OnPropertyChanged(nameof(CanMergeCurrentTab)); OnPropertyChanged(nameof(CanDeleteCurrentTab)); OnPropertyChanged(nameof(CanEditCurrentTab)); OnPropertyChanged(nameof(CurrentSelectedCount)); }
    }

    public int SelectedCategoriesCount
    {
        get => _selectedCategoriesCount;
        set { SetProperty(ref _selectedCategoriesCount, value); OnPropertyChanged(nameof(CanMergeCurrentTab)); OnPropertyChanged(nameof(CanDeleteCurrentTab)); OnPropertyChanged(nameof(CanEditCurrentTab)); OnPropertyChanged(nameof(CurrentSelectedCount)); }
    }

    public int SelectedTagsCount
    {
        get => _selectedTagsCount;
        set { SetProperty(ref _selectedTagsCount, value); OnPropertyChanged(nameof(CanMergeCurrentTab)); OnPropertyChanged(nameof(CanDeleteCurrentTab)); OnPropertyChanged(nameof(CanEditCurrentTab)); OnPropertyChanged(nameof(CurrentSelectedCount)); }
    }

    public int SelectedEventsCount
    {
        get => _selectedEventsCount;
        set { SetProperty(ref _selectedEventsCount, value); OnPropertyChanged(nameof(CanMergeCurrentTab)); OnPropertyChanged(nameof(CanDeleteCurrentTab)); OnPropertyChanged(nameof(CanEditCurrentTab)); OnPropertyChanged(nameof(CurrentSelectedCount)); }
    }

    public int CurrentSelectedCount => SelectedTabIndex switch
    {
        0 => SelectedAthletesCount,
        1 => SelectedPlacesCount,
        2 => SelectedCategoriesCount,
        3 => SelectedTagsCount,
        4 => SelectedEventsCount,
        _ => 0
    };

    public bool CanMergeCurrentTab => CurrentSelectedCount >= 2;

    #endregion

    #region Commands

    public ICommand RefreshCommand { get; }
    public ICommand SelectTabCommand { get; }
    public ICommand MergeSelectedCommand { get; }
    
    // Atletas
    public ICommand ToggleAthleteSelectionCommand { get; }
    public ICommand ConsolidateAthletesCommand { get; }
    
    // Lugares
    public ICommand TogglePlaceSelectionCommand { get; }
    
    // Categorías
    public ICommand ToggleCategorySelectionCommand { get; }
    
    // Tags
    public ICommand ToggleTagSelectionCommand { get; }
    
    // Tipos de Sesión -> Eventos
    public ICommand ToggleEventSelectionCommand { get; }
    
    // Acciones generales
    public ICommand DeleteSelectedCommand { get; }
    public ICommand EditSelectedCommand { get; }
    public ICommand AddNewCommand { get; }
    
    // Propiedades para habilitar botones
    public bool CanDeleteCurrentTab => CurrentSelectedCount >= 1;
    public bool CanEditCurrentTab => CurrentSelectedCount == 1;

    #endregion

    public DatabaseManagementViewModel(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        Title = "Gestión de Base de Datos";

        RefreshCommand = new AsyncRelayCommand(LoadAllDataAsync);
        SelectTabCommand = new Command<string>(SelectTab);
        MergeSelectedCommand = new AsyncRelayCommand(MergeSelectedAsync);
        
        // Atletas
        ToggleAthleteSelectionCommand = new Command<Athlete>(ToggleAthleteSelection);
        ConsolidateAthletesCommand = new AsyncRelayCommand(ConsolidateAthletesAsync);
        
        // Lugares
        TogglePlaceSelectionCommand = new Command<Place>(TogglePlaceSelection);
        
        // Categorías
        ToggleCategorySelectionCommand = new Command<Category>(ToggleCategorySelection);
        
        // Tags
        ToggleTagSelectionCommand = new Command<Tag>(ToggleTagSelection);
        
        // Eventos
        ToggleEventSelectionCommand = new Command<EventTagDefinition>(ToggleEventSelection);
        
        // Acciones generales
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync);
        EditSelectedCommand = new AsyncRelayCommand(EditSelectedAsync);
        AddNewCommand = new AsyncRelayCommand(AddNewAsync);
    }

    #region Data Loading

    public async Task LoadAllDataAsync()
    {
        System.Diagnostics.Debug.WriteLine($"[LoadAllDataAsync] Iniciando... IsBusy={IsBusy}");
        
        if (IsBusy) 
        {
            System.Diagnostics.Debug.WriteLine("[LoadAllDataAsync] Ya está ocupado, saliendo");
            return;
        }

        try
        {
            IsBusy = true;
            
            System.Diagnostics.Debug.WriteLine("[LoadAllDataAsync] Cargando atletas...");
            // Cargar secuencialmente para evitar problemas con CollectionView en MacCatalyst
            await LoadAthletesAsync();
            
            System.Diagnostics.Debug.WriteLine("[LoadAllDataAsync] Cargando lugares...");
            await LoadPlacesAsync();
            
            System.Diagnostics.Debug.WriteLine("[LoadAllDataAsync] Cargando categorías...");
            await LoadCategoriesAsync();
            
            System.Diagnostics.Debug.WriteLine("[LoadAllDataAsync] Cargando tags...");
            await LoadTagsAsync();
            
            System.Diagnostics.Debug.WriteLine("[LoadAllDataAsync] Cargando eventos...");
            await LoadEventsAsync();
            
            System.Diagnostics.Debug.WriteLine("[LoadAllDataAsync] Completado");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadAllDataAsync] ERROR: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[LoadAllDataAsync] StackTrace: {ex.StackTrace}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadAthletesAsync()
    {
        var athletes = await _databaseService.GetAllAthletesAsync();
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Athletes.Clear();
            foreach (var athlete in athletes.OrderBy(a => a.Apellido).ThenBy(a => a.Nombre))
            {
                Athletes.Add(athlete);
            }
            UpdateAthletesSelectedCount();
        });
    }

    private async Task LoadPlacesAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[LoadPlacesAsync] Iniciando carga...");
            var places = await _databaseService.GetAllPlacesAsync();
            System.Diagnostics.Debug.WriteLine($"[LoadPlacesAsync] Recibidos {places.Count} lugares");
            
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Places.Clear();
                foreach (var place in places)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadPlacesAsync] Añadiendo: ID={place.Id}, Nombre='{place.NombreLugar}'");
                    Places.Add(place);
                }
                System.Diagnostics.Debug.WriteLine($"[LoadPlacesAsync] Total en colección: {Places.Count}");
                UpdatePlacesSelectedCount();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadPlacesAsync] ERROR: {ex.Message}");
        }
    }

    private async Task LoadCategoriesAsync()
    {
        var categories = await _databaseService.GetAllCategoriesAsync();
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Categories.Clear();
            foreach (var category in categories.OrderBy(c => c.NombreCategoria))
            {
                Categories.Add(category);
            }
            UpdateCategoriesSelectedCount();
        });
    }

    private async Task LoadTagsAsync()
    {
        var tags = await _databaseService.GetAllTagsAsync();
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Tags.Clear();
            foreach (var tag in tags.OrderBy(t => t.NombreTag))
            {
                Tags.Add(tag);
            }
            UpdateTagsSelectedCount();
        });
    }

    private async Task LoadEventsAsync()
    {
        var events = await _databaseService.GetAllEventTagsAsync();
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Events.Clear();
            foreach (var evt in events)
            {
                Events.Add(evt);
            }
            UpdateEventsSelectedCount();
        });
    }

    #endregion

    #region Tab Selection

    private void SelectTab(string? tabIndex)
    {
        if (int.TryParse(tabIndex, out var index))
        {
            SelectedTabIndex = index;
        }
    }

    #endregion

    #region Toggle Selection

    private void ToggleAthleteSelection(Athlete? athlete)
    {
        if (athlete == null) return;
        athlete.IsSelected = !athlete.IsSelected;
        UpdateAthletesSelectedCount();
    }

    private void TogglePlaceSelection(Place? place)
    {
        if (place == null) return;
        place.IsSelectedForMerge = !place.IsSelectedForMerge;
        UpdatePlacesSelectedCount();
    }

    private void ToggleCategorySelection(Category? category)
    {
        if (category == null) return;
        category.IsSelectedForMerge = !category.IsSelectedForMerge;
        UpdateCategoriesSelectedCount();
    }

    private void ToggleTagSelection(Tag? tag)
    {
        if (tag == null) return;
        tag.IsSelectedForMerge = !tag.IsSelectedForMerge;
        UpdateTagsSelectedCount();
    }

    private void ToggleEventSelection(EventTagDefinition? eventTag)
    {
        if (eventTag == null) return;
        eventTag.IsSelected = !eventTag.IsSelected;
        UpdateEventsSelectedCount();
    }

    public void UpdateAthletesSelectedCount() => SelectedAthletesCount = Athletes.Count(a => a.IsSelected);
    public void UpdatePlacesSelectedCount() => SelectedPlacesCount = Places.Count(p => p.IsSelectedForMerge);
    public void UpdateCategoriesSelectedCount() => SelectedCategoriesCount = Categories.Count(c => c.IsSelectedForMerge);
    public void UpdateTagsSelectedCount() => SelectedTagsCount = Tags.Count(t => t.IsSelectedForMerge);
    public void UpdateEventsSelectedCount() => SelectedEventsCount = Events.Count(e => e.IsSelected);

    #endregion

    #region Merge Operations

    private async Task MergeSelectedAsync()
    {
        switch (SelectedTabIndex)
        {
            case 0: await MergeAthletesAsync(); break;
            case 1: await MergePlacesAsync(); break;
            case 2: await MergeCategoriesAsync(); break;
            case 3: await MergeTagsAsync(); break;
            case 4: await MergeEventsAsync(); break;
        }
    }

    private async Task MergeAthletesAsync()
    {
        var selected = Athletes.Where(a => a.IsSelected).OrderBy(a => a.Id).ToList();
        if (selected.Count < 2)
        {
            await Shell.Current.DisplayAlert("Aviso", "Selecciona al menos 2 atletas para fusionar.", "OK");
            return;
        }

        var options = selected
            .Select(a => $"{a.Nombre} {a.Apellido}".Trim())
            .Distinct()
            .ToArray();

        var chosenName = await Shell.Current.DisplayActionSheet(
            "¿Qué nombre quieres usar para este atleta?",
            "Cancelar", null, options);

        if (string.IsNullOrEmpty(chosenName) || chosenName == "Cancelar") return;

        var parts = chosenName.Split(' ', 2);
        var nombre = parts.Length > 0 ? parts[0] : "";
        var apellido = parts.Length > 1 ? parts[1] : "";

        try
        {
            IsBusy = true;
            var keepAthlete = selected.First();
            keepAthlete.Nombre = nombre;
            keepAthlete.Apellido = apellido;
            await _databaseService.SaveAthleteAsync(keepAthlete);

            var duplicates = selected.Skip(1).Select(a => a.Id).ToList();
            var merged = await _databaseService.MergeAthletesAsync(keepAthlete.Id, duplicates);

            await Shell.Current.DisplayAlert("Fusión completada",
                $"Se han fusionado {merged + 1} atletas bajo el nombre '{chosenName}'.", "OK");
            await LoadAthletesAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Error al fusionar: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MergePlacesAsync()
    {
        var selected = Places.Where(p => p.IsSelectedForMerge).OrderBy(p => p.Id).ToList();
        if (selected.Count < 2)
        {
            await Shell.Current.DisplayAlert("Aviso", "Selecciona al menos 2 lugares para fusionar.", "OK");
            return;
        }

        var options = selected
            .Select(p => p.NombreLugar ?? $"Lugar {p.Id}")
            .Distinct()
            .ToArray();

        var chosenName = await Shell.Current.DisplayActionSheet(
            "¿Qué nombre quieres usar para este lugar?",
            "Cancelar", null, options);

        if (string.IsNullOrEmpty(chosenName) || chosenName == "Cancelar") return;

        try
        {
            IsBusy = true;
            var keepPlace = selected.First(p => p.NombreLugar == chosenName) ?? selected.First();
            keepPlace.NombreLugar = chosenName;
            await _databaseService.SavePlaceAsync(keepPlace);

            var duplicates = selected.Where(p => p.Id != keepPlace.Id).Select(p => p.Id).ToList();
            var merged = await _databaseService.MergePlacesAsync(keepPlace.Id, duplicates);

            await Shell.Current.DisplayAlert("Fusión completada",
                $"Se han fusionado {merged + 1} lugares bajo el nombre '{chosenName}'.", "OK");
            await LoadPlacesAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Error al fusionar: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MergeCategoriesAsync()
    {
        var selected = Categories.Where(c => c.IsSelectedForMerge).OrderBy(c => c.Id).ToList();
        if (selected.Count < 2)
        {
            await Shell.Current.DisplayAlert("Aviso", "Selecciona al menos 2 categorías para fusionar.", "OK");
            return;
        }

        var options = selected
            .Select(c => c.NombreCategoria ?? $"Categoría {c.Id}")
            .Distinct()
            .ToArray();

        var chosenName = await Shell.Current.DisplayActionSheet(
            "¿Qué nombre quieres usar para esta categoría?",
            "Cancelar", null, options);

        if (string.IsNullOrEmpty(chosenName) || chosenName == "Cancelar") return;

        try
        {
            IsBusy = true;
            var keepCategory = selected.FirstOrDefault(c => c.NombreCategoria == chosenName) ?? selected.First();
            keepCategory.NombreCategoria = chosenName;
            await _databaseService.SaveCategoryAsync(keepCategory);

            var duplicates = selected.Where(c => c.Id != keepCategory.Id).Select(c => c.Id).ToList();
            var merged = await _databaseService.MergeCategoriesAsync(keepCategory.Id, duplicates);

            await Shell.Current.DisplayAlert("Fusión completada",
                $"Se han fusionado {merged + 1} categorías bajo el nombre '{chosenName}'.", "OK");
            await LoadCategoriesAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Error al fusionar: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MergeTagsAsync()
    {
        var selected = Tags.Where(t => t.IsSelectedForMerge).OrderBy(t => t.Id).ToList();
        if (selected.Count < 2)
        {
            await Shell.Current.DisplayAlert("Aviso", "Selecciona al menos 2 tags para fusionar.", "OK");
            return;
        }

        var options = selected
            .Select(t => t.NombreTag ?? $"Tag {t.Id}")
            .Distinct()
            .ToArray();

        var chosenName = await Shell.Current.DisplayActionSheet(
            "¿Qué nombre quieres usar para este tag?",
            "Cancelar", null, options);

        if (string.IsNullOrEmpty(chosenName) || chosenName == "Cancelar") return;

        try
        {
            IsBusy = true;
            var keepTag = selected.FirstOrDefault(t => t.NombreTag == chosenName) ?? selected.First();
            // No actualizamos el nombre porque podría afectar a la lógica del tag

            var duplicates = selected.Where(t => t.Id != keepTag.Id).Select(t => t.Id).ToList();
            var merged = await _databaseService.MergeTagsAsync(keepTag.Id, duplicates);

            await Shell.Current.DisplayAlert("Fusión completada",
                $"Se han fusionado {merged + 1} tags bajo el nombre '{chosenName}'.", "OK");
            await LoadTagsAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Error al fusionar: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MergeEventsAsync()
    {
        var selected = Events.Where(e => e.IsSelected).OrderBy(e => e.Id).ToList();
        if (selected.Count < 2)
        {
            await Shell.Current.DisplayAlert("Aviso", "Selecciona al menos 2 eventos para fusionar.", "OK");
            return;
        }

        // Verificar que no haya eventos de sistema seleccionados
        if (selected.Any(e => e.IsSystem))
        {
            await Shell.Current.DisplayAlert("Aviso", "No se pueden fusionar eventos de sistema.", "OK");
            return;
        }

        var options = selected
            .Select(e => e.Nombre ?? $"Evento {e.Id}")
            .Distinct()
            .ToArray();

        var chosenName = await Shell.Current.DisplayActionSheet(
            "¿Qué nombre quieres usar para este evento?",
            "Cancelar", null, options);

        if (string.IsNullOrEmpty(chosenName) || chosenName == "Cancelar") return;

        try
        {
            IsBusy = true;
            var keepEvent = selected.FirstOrDefault(e => e.Nombre == chosenName) ?? selected.First();
            keepEvent.Nombre = chosenName;
            await _databaseService.SaveEventTagAsync(keepEvent);

            var duplicates = selected.Where(e => e.Id != keepEvent.Id).Select(e => e.Id).ToList();
            var merged = await _databaseService.MergeEventTagsAsync(keepEvent.Id, duplicates);

            await Shell.Current.DisplayAlert("Fusión completada",
                $"Se han fusionado {merged + 1} eventos bajo el nombre '{chosenName}'.", "OK");
            await LoadEventsAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Error al fusionar: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConsolidateAthletesAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            var duplicatesRemoved = await _databaseService.ConsolidateDuplicateAthletesAsync();

            if (duplicatesRemoved > 0)
            {
                await Shell.Current.DisplayAlert("Consolidación",
                    $"Se han consolidado {duplicatesRemoved} atletas duplicados.", "OK");
            }
            else
            {
                await Shell.Current.DisplayAlert("Consolidación",
                    "No se encontraron atletas duplicados.", "OK");
            }

            await LoadAthletesAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Error al consolidar: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region Delete & Edit Operations

    private async Task DeleteSelectedAsync()
    {
        if (IsBusy || CurrentSelectedCount < 1) return;

        var entityName = SelectedTabIndex switch
        {
            0 => CurrentSelectedCount == 1 ? "atleta" : "atletas",
            1 => CurrentSelectedCount == 1 ? "lugar" : "lugares",
            2 => CurrentSelectedCount == 1 ? "categoría" : "categorías",
            3 => CurrentSelectedCount == 1 ? "tag" : "tags",
            4 => CurrentSelectedCount == 1 ? "evento" : "eventos",
            _ => "elementos"
        };

        var confirm = await Shell.Current.DisplayAlert(
            "Confirmar eliminación",
            $"¿Estás seguro de que deseas eliminar {CurrentSelectedCount} {entityName}? Esta acción no se puede deshacer.",
            "Eliminar", "Cancelar");

        if (!confirm) return;

        try
        {
            IsBusy = true;

            switch (SelectedTabIndex)
            {
                case 0: await DeleteSelectedAthletesAsync(); break;
                case 1: await DeleteSelectedPlacesAsync(); break;
                case 2: await DeleteSelectedCategoriesAsync(); break;
                case 3: await DeleteSelectedTagsAsync(); break;
                case 4: await DeleteSelectedEventsAsync(); break;
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Error al eliminar: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteSelectedAthletesAsync()
    {
        var selected = Athletes.Where(a => a.IsSelected).ToList();
        foreach (var athlete in selected)
        {
            await _databaseService.DeleteAthleteAsync(athlete);
        }
        await Shell.Current.DisplayAlert("Eliminación completada", $"Se han eliminado {selected.Count} atleta(s).", "OK");
        await LoadAthletesAsync();
    }

    private async Task DeleteSelectedPlacesAsync()
    {
        var selected = Places.Where(p => p.IsSelectedForMerge).ToList();
        foreach (var place in selected)
        {
            await _databaseService.DeletePlaceAsync(place);
        }
        await Shell.Current.DisplayAlert("Eliminación completada", $"Se han eliminado {selected.Count} lugar(es).", "OK");
        await LoadPlacesAsync();
    }

    private async Task DeleteSelectedCategoriesAsync()
    {
        var selected = Categories.Where(c => c.IsSelectedForMerge).ToList();
        foreach (var category in selected)
        {
            await _databaseService.DeleteCategoryAsync(category);
        }
        await Shell.Current.DisplayAlert("Eliminación completada", $"Se han eliminado {selected.Count} categoría(s).", "OK");
        await LoadCategoriesAsync();
    }

    private async Task DeleteSelectedTagsAsync()
    {
        var selected = Tags.Where(t => t.IsSelectedForMerge).ToList();
        foreach (var tag in selected)
        {
            await _databaseService.DeleteTagAsync(tag);
        }
        await Shell.Current.DisplayAlert("Eliminación completada", $"Se han eliminado {selected.Count} tag(s).", "OK");
        await LoadTagsAsync();
    }

    private async Task DeleteSelectedEventsAsync()
    {
        var selected = Events.Where(e => e.IsSelected).ToList();
        
        // Verificar si hay eventos de sistema seleccionados
        var systemEvents = selected.Where(e => e.IsSystem).ToList();
        if (systemEvents.Count > 0)
        {
            await Shell.Current.DisplayAlert("Aviso", 
                $"No se pueden eliminar {systemEvents.Count} evento(s) de sistema.", "OK");
            selected = selected.Where(e => !e.IsSystem).ToList();
            if (selected.Count == 0) return;
        }
        
        foreach (var evt in selected)
        {
            await _databaseService.DeleteEventTagAsync(evt.Id);
        }
        await Shell.Current.DisplayAlert("Eliminación completada", $"Se han eliminado {selected.Count} evento(s).", "OK");
        await LoadEventsAsync();
    }

    private async Task EditSelectedAsync()
    {
        if (IsBusy || CurrentSelectedCount != 1) return;

        try
        {
            IsBusy = true;

            switch (SelectedTabIndex)
            {
                case 0: await EditSelectedAthleteAsync(); break;
                case 1: await EditSelectedPlaceAsync(); break;
                case 2: await EditSelectedCategoryAsync(); break;
                case 3: await EditSelectedTagAsync(); break;
                case 4: await EditSelectedEventAsync(); break;
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Error al editar: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task EditSelectedAthleteAsync()
    {
        var athlete = Athletes.FirstOrDefault(a => a.IsSelected);
        if (athlete == null) return;

        var newName = await Shell.Current.DisplayPromptAsync(
            "Editar Atleta",
            "Nombre:",
            initialValue: athlete.Nombre ?? "",
            placeholder: "Nombre");

        if (string.IsNullOrEmpty(newName)) return;

        var newSurname = await Shell.Current.DisplayPromptAsync(
            "Editar Atleta",
            "Apellido:",
            initialValue: athlete.Apellido ?? "",
            placeholder: "Apellido");

        if (newSurname == null) return; // Canceló

        athlete.Nombre = newName;
        athlete.Apellido = newSurname;
        await _databaseService.SaveAthleteAsync(athlete);
        await Shell.Current.DisplayAlert("Editado", "Atleta actualizado correctamente.", "OK");
        await LoadAthletesAsync();
    }

    private async Task EditSelectedPlaceAsync()
    {
        var place = Places.FirstOrDefault(p => p.IsSelectedForMerge);
        if (place == null) return;

        var newName = await Shell.Current.DisplayPromptAsync(
            "Editar Lugar",
            "Nombre del lugar:",
            initialValue: place.NombreLugar ?? "",
            placeholder: "Nombre");

        if (string.IsNullOrEmpty(newName)) return;

        place.NombreLugar = newName;
        await _databaseService.SavePlaceAsync(place);
        await Shell.Current.DisplayAlert("Editado", "Lugar actualizado correctamente.", "OK");
        await LoadPlacesAsync();
    }

    private async Task EditSelectedCategoryAsync()
    {
        var category = Categories.FirstOrDefault(c => c.IsSelectedForMerge);
        if (category == null) return;

        var newName = await Shell.Current.DisplayPromptAsync(
            "Editar Categoría",
            "Nombre de la categoría:",
            initialValue: category.NombreCategoria ?? "",
            placeholder: "Nombre");

        if (string.IsNullOrEmpty(newName)) return;

        category.NombreCategoria = newName;
        await _databaseService.SaveCategoryAsync(category);
        await Shell.Current.DisplayAlert("Editado", "Categoría actualizada correctamente.", "OK");
        await LoadCategoriesAsync();
    }

    private async Task EditSelectedTagAsync()
    {
        var tag = Tags.FirstOrDefault(t => t.IsSelectedForMerge);
        if (tag == null) return;

        var newName = await Shell.Current.DisplayPromptAsync(
            "Editar Tag",
            "Nombre del tag:",
            initialValue: tag.NombreTag ?? "",
            placeholder: "Nombre");

        if (string.IsNullOrEmpty(newName)) return;

        tag.NombreTag = newName;
        await _databaseService.SaveTagAsync(tag);
        await Shell.Current.DisplayAlert("Editado", "Tag actualizado correctamente.", "OK");
        await LoadTagsAsync();
    }

    private async Task EditSelectedEventAsync()
    {
        var eventTag = Events.FirstOrDefault(e => e.IsSelected);
        if (eventTag == null) return;

        if (eventTag.IsSystem)
        {
            await Shell.Current.DisplayAlert("Aviso", "No se pueden editar eventos de sistema.", "OK");
            return;
        }

        var newName = await Shell.Current.DisplayPromptAsync(
            "Editar Evento",
            "Nombre del evento:",
            initialValue: eventTag.Nombre ?? "",
            placeholder: "Nombre");

        if (string.IsNullOrEmpty(newName)) return;

        eventTag.Nombre = newName;
        await _databaseService.SaveEventTagAsync(eventTag);
        await Shell.Current.DisplayAlert("Editado", "Evento actualizado correctamente.", "OK");
        await LoadEventsAsync();
    }

    #endregion

    #region Add New Operations

    private async Task AddNewAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;

            switch (SelectedTabIndex)
            {
                case 0: await AddNewAthleteAsync(); break;
                case 1: await AddNewPlaceAsync(); break;
                case 2: await AddNewCategoryAsync(); break;
                case 3: await AddNewTagAsync(); break;
                case 4: await AddNewEventAsync(); break;
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Error al añadir: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddNewAthleteAsync()
    {
        var nombre = await Shell.Current.DisplayPromptAsync(
            "Nuevo Atleta",
            "Nombre:",
            placeholder: "Nombre");

        if (string.IsNullOrEmpty(nombre)) return;

        var apellido = await Shell.Current.DisplayPromptAsync(
            "Nuevo Atleta",
            "Apellido:",
            placeholder: "Apellido");

        if (apellido == null) return; // Canceló

        var athlete = new Athlete
        {
            Nombre = nombre,
            Apellido = apellido
        };

        await _databaseService.SaveAthleteAsync(athlete);
        await Shell.Current.DisplayAlert("Añadido", "Atleta añadido correctamente.", "OK");
        await LoadAthletesAsync();
    }

    private async Task AddNewPlaceAsync()
    {
        var nombre = await Shell.Current.DisplayPromptAsync(
            "Nuevo Lugar",
            "Nombre del lugar:",
            placeholder: "Nombre");

        if (string.IsNullOrEmpty(nombre)) return;

        var place = new Place
        {
            NombreLugar = nombre
        };

        await _databaseService.SavePlaceAsync(place);
        await Shell.Current.DisplayAlert("Añadido", "Lugar añadido correctamente.", "OK");
        await LoadPlacesAsync();
    }

    private async Task AddNewCategoryAsync()
    {
        var nombre = await Shell.Current.DisplayPromptAsync(
            "Nueva Categoría",
            "Nombre de la categoría:",
            placeholder: "Nombre");

        if (string.IsNullOrEmpty(nombre)) return;

        var category = new Category
        {
            NombreCategoria = nombre
        };

        await _databaseService.SaveCategoryAsync(category);
        await Shell.Current.DisplayAlert("Añadido", "Categoría añadida correctamente.", "OK");
        await LoadCategoriesAsync();
    }

    private async Task AddNewTagAsync()
    {
        var nombre = await Shell.Current.DisplayPromptAsync(
            "Nuevo Tag",
            "Nombre del tag:",
            placeholder: "Nombre");

        if (string.IsNullOrEmpty(nombre)) return;

        var tag = new Tag
        {
            NombreTag = nombre
        };

        await _databaseService.SaveTagAsync(tag);
        await Shell.Current.DisplayAlert("Añadido", "Tag añadido correctamente.", "OK");
        await LoadTagsAsync();
    }

    private async Task AddNewEventAsync()
    {
        var nombre = await Shell.Current.DisplayPromptAsync(
            "Nuevo Evento",
            "Nombre del evento:",
            placeholder: "Nombre");

        if (string.IsNullOrEmpty(nombre)) return;

        var eventTag = new EventTagDefinition
        {
            Nombre = nombre,
            IsSystem = false
        };

        await _databaseService.SaveEventTagAsync(eventTag);
        await Shell.Current.DisplayAlert("Añadido", "Evento añadido correctamente.", "OK");
        await LoadEventsAsync();
    }

    #endregion
}
