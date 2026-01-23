using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// ViewModel para la página de perfil del usuario "Yo"
/// </summary>
public class UserProfileViewModel : BaseViewModel
{
    private readonly DatabaseService _databaseService;
    private readonly UserProfileNotifier _userProfileNotifier;
    private readonly ICloudBackendService _cloudBackendService;
    private UserProfile? _profile;
    private bool _isEditing;
    private string? _statusMessage;
    private bool _hasChanges;

    private string? _cloudEmail;
    private string? _cloudPassword;
    private string? _cloudStatusMessage;
    private bool _isCloudBusy;

    // Campos editables
    private string? _nombre;
    private string? _apellidos;
    private DateTime? _fechaNacimiento;
    private double? _peso;
    private double? _altura;
    private string? _categoria;
    private string? _sexo;
    private string? _club;
    private string? _manoHabil;
    private string? _notas;
    private string? _fotoPath;
    private int? _referenceAthleteId;
    private string? _referenceAthleteDisplay;

    private static readonly char[] CategoriaSeparators = new[] { ';', ',', '|' };

    public UserProfile? Profile
    {
        get => _profile;
        private set => SetProperty(ref _profile, value);
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set 
        { 
            if (SetProperty(ref _statusMessage, value))
                OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public string? CloudEmail
    {
        get => _cloudEmail;
        set => SetProperty(ref _cloudEmail, value);
    }

    public string? CloudPassword
    {
        get => _cloudPassword;
        set => SetProperty(ref _cloudPassword, value);
    }

    public string? CloudStatusMessage
    {
        get => _cloudStatusMessage;
        set
        {
            if (SetProperty(ref _cloudStatusMessage, value))
                OnPropertyChanged(nameof(HasCloudStatusMessage));
        }
    }

    public bool HasCloudStatusMessage => !string.IsNullOrWhiteSpace(CloudStatusMessage);

    public bool IsCloudBusy
    {
        get => _isCloudBusy;
        set => SetProperty(ref _isCloudBusy, value);
    }

    public bool IsCloudAuthenticated => _cloudBackendService.IsAuthenticated;
    public string CloudUserName => _cloudBackendService.CurrentUserName ?? string.Empty;
    public string CloudTeamName => _cloudBackendService.TeamName ?? string.Empty;
    public string CloudUserRole => _cloudBackendService.CurrentUserRole ?? string.Empty;

    public bool HasChanges
    {
        get => _hasChanges;
        private set => SetProperty(ref _hasChanges, value);
    }

    // Propiedades editables con notificación de cambios
    public string? Nombre
    {
        get => _nombre;
        set { if (SetProperty(ref _nombre, value)) MarkAsChanged(); }
    }

    public string? Apellidos
    {
        get => _apellidos;
        set { if (SetProperty(ref _apellidos, value)) MarkAsChanged(); }
    }

    public DateTime? FechaNacimiento
    {
        get => _fechaNacimiento;
        set { if (SetProperty(ref _fechaNacimiento, value)) { MarkAsChanged(); OnPropertyChanged(nameof(EdadTexto)); OnPropertyChanged(nameof(FechaNacimientoDate)); } }
    }

    // Propiedad para el DatePicker (no acepta null)
    public DateTime FechaNacimientoDate
    {
        get => _fechaNacimiento ?? DateTime.Today;
        set 
        { 
            FechaNacimiento = value;
        }
    }

    public double? Peso
    {
        get => _peso;
        set { if (SetProperty(ref _peso, value)) { MarkAsChanged(); OnPropertyChanged(nameof(IMCTexto)); } }
    }

    public double? Altura
    {
        get => _altura;
        set { if (SetProperty(ref _altura, value)) { MarkAsChanged(); OnPropertyChanged(nameof(IMCTexto)); } }
    }

    public string? Categoria
    {
        get => _categoria;
        set { if (SetProperty(ref _categoria, value)) MarkAsChanged(); }
    }

    public string? Sexo
    {
        get => _sexo;
        set { if (SetProperty(ref _sexo, value)) MarkAsChanged(); }
    }

    public string? Club
    {
        get => _club;
        set { if (SetProperty(ref _club, value)) MarkAsChanged(); }
    }

    public string? ManoHabil
    {
        get => _manoHabil;
        set { if (SetProperty(ref _manoHabil, value)) MarkAsChanged(); }
    }

    public string? Notas
    {
        get => _notas;
        set { if (SetProperty(ref _notas, value)) MarkAsChanged(); }
    }

    public string? FotoPath
    {
        get => _fotoPath;
        set 
        { 
            if (SetProperty(ref _fotoPath, value)) 
            {
                MarkAsChanged(); 
                OnPropertyChanged(nameof(HasFoto));
            }
        }
    }

    public bool HasFoto => !string.IsNullOrWhiteSpace(FotoPath);

    // Propiedades calculadas
    public string EdadTexto
    {
        get
        {
            if (!FechaNacimiento.HasValue) return "—";
            var today = DateTime.Today;
            var age = today.Year - FechaNacimiento.Value.Year;
            if (FechaNacimiento.Value.Date > today.AddYears(-age)) age--;
            return $"{age} años";
        }
    }

    public string IMCTexto
    {
        get
        {
            if (!Peso.HasValue || !Altura.HasValue || Altura.Value <= 0) return "—";
            var alturaMetros = Altura.Value / 100.0;
            var imc = Math.Round(Peso.Value / (alturaMetros * alturaMetros), 1);
            return $"{imc}";
        }
    }

    // Listas para selectores
    public ObservableCollection<SelectableOption> CategoriaOptions { get; } = new();
    public ObservableCollection<SelectableOption> SexoOptions { get; } = new();
    public ObservableCollection<SelectableOption> ManoHabilOptions { get; } = new();
    public ObservableCollection<Athlete> AthleteOptions { get; } = new();

    // Comandos
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SelectPhotoCommand { get; }
    public ICommand TakePhotoCommand { get; }
    public ICommand RemovePhotoCommand { get; }
    public ICommand SelectCategoriaCommand { get; }
    public ICommand SelectSexoCommand { get; }
    public ICommand SelectManoHabilCommand { get; }
    public ICommand SelectReferenceAthleteCommand { get; }
    public ICommand CloudLoginCommand { get; }
    public ICommand CloudLogoutCommand { get; }

    public UserProfileViewModel(DatabaseService databaseService, UserProfileNotifier userProfileNotifier, ICloudBackendService cloudBackendService)
    {
        _databaseService = databaseService;
        _userProfileNotifier = userProfileNotifier;
        _cloudBackendService = cloudBackendService;
        Title = "Mi Perfil";

        // Inicializar opciones
        foreach (var item in PaddlingCategories.All)
            CategoriaOptions.Add(new SelectableOption { Value = item });
        foreach (var item in SexOptions.All)
            SexoOptions.Add(new SelectableOption { Value = item });
        foreach (var item in HandOptions.All)
            ManoHabilOptions.Add(new SelectableOption { Value = item });

        SaveCommand = new AsyncRelayCommand(SaveProfileAsync);
        CancelCommand = new RelayCommand(CancelEditing);
        SelectPhotoCommand = new AsyncRelayCommand(SelectPhotoAsync);
        TakePhotoCommand = new AsyncRelayCommand(TakePhotoAsync);
        RemovePhotoCommand = new RelayCommand(RemovePhoto);
        SelectCategoriaCommand = new RelayCommand<SelectableOption>(SelectCategoria);
        SelectSexoCommand = new RelayCommand<SelectableOption>(SelectSexo);
        SelectManoHabilCommand = new RelayCommand<SelectableOption>(SelectManoHabil);
        SelectReferenceAthleteCommand = new AsyncRelayCommand(SelectReferenceAthleteAsync);
        CloudLoginCommand = new AsyncRelayCommand(LoginCloudAsync);
        CloudLogoutCommand = new AsyncRelayCommand(LogoutCloudAsync);
    }

    private void NotifyCloudStateChanged()
    {
        OnPropertyChanged(nameof(IsCloudAuthenticated));
        OnPropertyChanged(nameof(CloudUserName));
        OnPropertyChanged(nameof(CloudTeamName));
        OnPropertyChanged(nameof(CloudUserRole));
    }

    private async Task LoginCloudAsync()
    {
        if (string.IsNullOrWhiteSpace(CloudEmail) || string.IsNullOrWhiteSpace(CloudPassword))
        {
            CloudStatusMessage = "Email y contraseña son requeridos.";
            return;
        }

        try
        {
            IsCloudBusy = true;
            CloudStatusMessage = null;

            var result = await _cloudBackendService.LoginAsync(CloudEmail, CloudPassword);
            if (!result.Success)
            {
                CloudStatusMessage = result.ErrorMessage ?? "No se pudo iniciar sesión.";
                return;
            }

            CloudPassword = string.Empty;
            CloudStatusMessage = "Sesión iniciada correctamente.";
            NotifyCloudStateChanged();
        }
        catch (Exception ex)
        {
            CloudStatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsCloudBusy = false;
        }
    }

    private async Task LogoutCloudAsync()
    {
        try
        {
            IsCloudBusy = true;
            CloudStatusMessage = null;
            await _cloudBackendService.LogoutAsync();
            CloudStatusMessage = "Sesión cerrada.";
            NotifyCloudStateChanged();
        }
        catch (Exception ex)
        {
            CloudStatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsCloudBusy = false;
        }
    }

    public int? ReferenceAthleteId
    {
        get => _referenceAthleteId;
        set
        {
            if (SetProperty(ref _referenceAthleteId, value))
            {
                UpdateReferenceAthleteDisplay();
                MarkAsChanged();
            }
        }
    }

    public string ReferenceAthleteDisplay
    {
        get => string.IsNullOrWhiteSpace(_referenceAthleteDisplay) ? "Selecciona atleta" : _referenceAthleteDisplay;
        private set => SetProperty(ref _referenceAthleteDisplay, value);
    }

    private async Task EnsureAthletesLoadedAsync()
    {
        if (AthleteOptions.Count > 0) return;

        var athletes = await _databaseService.GetUniqueAthletesAsync();
        AthleteOptions.Clear();
        foreach (var a in athletes)
            AthleteOptions.Add(a);
    }

    private void UpdateReferenceAthleteDisplay()
    {
        if (!ReferenceAthleteId.HasValue)
        {
            ReferenceAthleteDisplay = "Selecciona atleta";
            return;
        }

        var match = AthleteOptions.FirstOrDefault(a => a.Id == ReferenceAthleteId.Value);
        ReferenceAthleteDisplay = match?.NombreCompleto ?? "Selecciona atleta";
    }

    private async Task SelectReferenceAthleteAsync()
    {
        try
        {
            await EnsureAthletesLoadedAsync();

            var names = AthleteOptions.Select(a => a.NombreCompleto).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (names.Length == 0)
            {
                StatusMessage = "No hay atletas en la base de datos todavía";
                return;
            }

            var selected = await Shell.Current.DisplayActionSheet(
                "¿Cuál de estos atletas eres tú?",
                "Cancelar",
                null,
                names);

            if (string.IsNullOrWhiteSpace(selected) || selected == "Cancelar")
                return;

            var athlete = AthleteOptions.FirstOrDefault(a => a.NombreCompleto == selected);
            if (athlete == null)
                return;

            _referenceAthleteId = athlete.Id;
            UpdateReferenceAthleteDisplay();
            OnPropertyChanged(nameof(ReferenceAthleteId));
            MarkAsChanged();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cargar atletas: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error selecting reference athlete: {ex}");
        }
    }

    private static HashSet<string> ParseCategoriaMulti(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var parts = value.Split(CategoriaSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new HashSet<string>(parts, StringComparer.OrdinalIgnoreCase);
    }

    private string? BuildCategoriaMultiFromOptions()
    {
        var selected = CategoriaOptions
            .Where(o => o.IsSelected)
            .Select(o => o.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        if (selected.Count == 0)
            return null;

        return string.Join(";", selected);
    }

    private void ApplyCategoriaSelectionFromSerialized(string? serialized)
    {
        var selected = ParseCategoriaMulti(serialized);
        foreach (var opt in CategoriaOptions)
            opt.IsSelected = selected.Contains(opt.Value);
    }

    private static void ApplySingleSelectionFromValue(IEnumerable<SelectableOption> options, string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        foreach (var opt in options)
        {
            opt.IsSelected = !string.IsNullOrWhiteSpace(normalized)
                && string.Equals(opt.Value?.Trim(), normalized, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void SelectCategoria(SelectableOption? option)
    {
        if (option == null) return;
        option.IsSelected = !option.IsSelected;
        Categoria = BuildCategoriaMultiFromOptions();
    }

    private void SelectSexo(SelectableOption? option)
    {
        if (option == null) return;
        foreach (var opt in SexoOptions)
            opt.IsSelected = opt == option;
        Sexo = option.Value;
    }

    private void SelectManoHabil(SelectableOption? option)
    {
        if (option == null) return;
        foreach (var opt in ManoHabilOptions)
            opt.IsSelected = opt == option;
        ManoHabil = option.Value;
    }

    private void MarkAsChanged()
    {
        HasChanges = true;
        StatusMessage = null;
    }

    public async Task LoadProfileAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            StatusMessage = null;

            await EnsureAthletesLoadedAsync();

            Profile = await _databaseService.GetUserProfileAsync();

            if (Profile != null)
            {
                // Cargar valores en los campos editables
                Nombre = Profile.Nombre;
                Apellidos = Profile.Apellidos;
                FechaNacimiento = Profile.FechaNacimientoDateTime;
                Peso = Profile.Peso;
                Altura = Profile.Altura;
                Categoria = Profile.Categoria;
                ApplyCategoriaSelectionFromSerialized(Profile.Categoria);
                Categoria = BuildCategoriaMultiFromOptions();
                Sexo = Profile.Sexo;
                ApplySingleSelectionFromValue(SexoOptions, Sexo);
                Club = Profile.Club;
                ManoHabil = Profile.ManoHabil;
                ApplySingleSelectionFromValue(ManoHabilOptions, ManoHabil);
                Notas = Profile.Notas;
                FotoPath = Profile.FotoPath;

                _referenceAthleteId = Profile.ReferenceAthleteId;
                UpdateReferenceAthleteDisplay();
                OnPropertyChanged(nameof(ReferenceAthleteId));
                OnPropertyChanged(nameof(ReferenceAthleteDisplay));
            }
            else
            {
                // Perfil nuevo
                ClearFields();
            }

            HasChanges = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cargar perfil: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error loading profile: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveProfileAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            StatusMessage = null;

            var profile = Profile ?? new UserProfile();
            
            profile.Nombre = Nombre;
            profile.Apellidos = Apellidos;
            profile.FechaNacimientoDateTime = FechaNacimiento;
            profile.Peso = Peso;
            profile.Altura = Altura;
            Categoria = BuildCategoriaMultiFromOptions();
            profile.Categoria = Categoria;
            profile.Sexo = Sexo;
            profile.Club = Club;
            profile.ManoHabil = ManoHabil;
            profile.Notas = Notas;
            profile.FotoPath = FotoPath;
            profile.ReferenceAthleteId = ReferenceAthleteId;
            profile.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            if (profile.Id == 0)
            {
                profile.CreatedAt = profile.UpdatedAt;
            }

            await _databaseService.SaveUserProfileAsync(profile);
            Profile = profile;
            
            HasChanges = false;
            StatusMessage = "Perfil guardado correctamente";

            _userProfileNotifier.NotifyProfileSaved();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al guardar: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error saving profile: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelEditing()
    {
        if (Profile != null)
        {
            // Restaurar valores originales
            Nombre = Profile.Nombre;
            Apellidos = Profile.Apellidos;
            FechaNacimiento = Profile.FechaNacimientoDateTime;
            Peso = Profile.Peso;
            Altura = Profile.Altura;
            Categoria = Profile.Categoria;
            ApplyCategoriaSelectionFromSerialized(Profile.Categoria);
            Categoria = BuildCategoriaMultiFromOptions();
            Sexo = Profile.Sexo;
            ApplySingleSelectionFromValue(SexoOptions, Sexo);
            Club = Profile.Club;
            ManoHabil = Profile.ManoHabil;
            ApplySingleSelectionFromValue(ManoHabilOptions, ManoHabil);
            Notas = Profile.Notas;
            FotoPath = Profile.FotoPath;

            _referenceAthleteId = Profile.ReferenceAthleteId;
            UpdateReferenceAthleteDisplay();
            OnPropertyChanged(nameof(ReferenceAthleteId));
            OnPropertyChanged(nameof(ReferenceAthleteDisplay));
        }
        else
        {
            ClearFields();
        }

        HasChanges = false;
        StatusMessage = null;
    }

    private void ClearFields()
    {
        _nombre = null;
        _apellidos = null;
        _fechaNacimiento = null;
        _peso = null;
        _altura = null;
        _categoria = null;
        _sexo = null;
        _club = null;
        _manoHabil = null;
        _notas = null;
        _fotoPath = null;
        _referenceAthleteId = null;
        _referenceAthleteDisplay = null;

        foreach (var opt in CategoriaOptions)
            opt.IsSelected = false;

        foreach (var opt in SexoOptions)
            opt.IsSelected = false;

        foreach (var opt in ManoHabilOptions)
            opt.IsSelected = false;

        OnPropertyChanged(nameof(Nombre));
        OnPropertyChanged(nameof(Apellidos));
        OnPropertyChanged(nameof(FechaNacimiento));
        OnPropertyChanged(nameof(Peso));
        OnPropertyChanged(nameof(Altura));
        OnPropertyChanged(nameof(Categoria));
        OnPropertyChanged(nameof(Sexo));
        OnPropertyChanged(nameof(Club));
        OnPropertyChanged(nameof(ManoHabil));
        OnPropertyChanged(nameof(Notas));
        OnPropertyChanged(nameof(FotoPath));
        OnPropertyChanged(nameof(EdadTexto));
        OnPropertyChanged(nameof(IMCTexto));
        OnPropertyChanged(nameof(ReferenceAthleteId));
        OnPropertyChanged(nameof(ReferenceAthleteDisplay));
    }

    private async Task SelectPhotoAsync()
    {
        try
        {
            var result = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions
            {
                Title = "Seleccionar foto de perfil"
            });

            if (result != null)
            {
                // Copiar a carpeta de la app
                var localPath = await CopyPhotoToAppStorageAsync(result);
                FotoPath = localPath;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al seleccionar foto: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error selecting photo: {ex}");
        }
    }

    private async Task TakePhotoAsync()
    {
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                StatusMessage = "La captura de fotos no está disponible en este dispositivo";
                return;
            }

            var result = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
            {
                Title = "Tomar foto de perfil"
            });

            if (result != null)
            {
                var localPath = await CopyPhotoToAppStorageAsync(result);
                FotoPath = localPath;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al tomar foto: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error taking photo: {ex}");
        }
    }

    private async Task<string> CopyPhotoToAppStorageAsync(FileResult photo)
    {
        var profilePhotosDir = Path.Combine(FileSystem.AppDataDirectory, "ProfilePhotos");
        Directory.CreateDirectory(profilePhotosDir);

        var fileName = $"profile_{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(photo.FileName)}";
        var localPath = Path.Combine(profilePhotosDir, fileName);

        using var sourceStream = await photo.OpenReadAsync();
        using var destStream = File.Create(localPath);
        await sourceStream.CopyToAsync(destStream);

        return localPath;
    }

    private void RemovePhoto()
    {
        // Opcionalmente eliminar el archivo físico
        if (!string.IsNullOrEmpty(FotoPath) && File.Exists(FotoPath))
        {
            try
            {
                File.Delete(FotoPath);
            }
            catch { /* Ignorar errores de eliminación */ }
        }

        FotoPath = null;
    }
}
