using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
#if IOS || MACCATALYST
using Foundation;
#endif
using Microsoft.Maui.Storage;
using Microsoft.Maui.Devices;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Servicio de backend para la nube.
/// Se comunica con el servidor backend que maneja las credenciales de Wasabi de forma segura.
/// El cliente NUNCA tiene acceso a las claves de Wasabi.
/// </summary>
public class CloudBackendService : ICloudBackendService
{
    private readonly HttpClient _httpClient;
    private string _baseUrl;
    
    private string? _accessToken;
    private DateTime? _tokenExpiresAt;
    private string? _refreshToken;
    
    // Almacenamiento seguro de tokens
    private const string AccessTokenKey = "CloudBackend_AccessToken";
    private const string RefreshTokenKey = "CloudBackend_RefreshToken";
    private const string TokenExpiresAtKey = "CloudBackend_TokenExpiresAt";
    private const string UserNameKey = "CloudBackend_UserName";
    private const string TeamNameKey = "CloudBackend_TeamName";
    private const string UserRoleKey = "CloudBackend_UserRole";
    private const string BaseUrlKeyPrefix = "CloudBackend_BaseUrl";
    private const string DeviceIdKey = "CloudBackend_DeviceId";

    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken) && 
                                    _tokenExpiresAt.HasValue && 
                                    _tokenExpiresAt.Value > DateTime.UtcNow;

    public string? CurrentUserName { get; private set; }
    public string? TeamName { get; private set; }
    public string? CurrentUserRole { get; private set; }
    public string BaseUrl => _baseUrl;

    public CloudBackendService()
    {
        // URL del backend - configurar según el entorno
        // En producción, esto debería venir de configuración
        _baseUrl = Preferences.Get(GetBaseUrlKey(), GetBackendUrl());
        if (_baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            _baseUrl.Contains("192.168.1.10", StringComparison.OrdinalIgnoreCase) ||
            _baseUrl.Contains("access853919316.webspace-data.io", StringComparison.OrdinalIgnoreCase) ||
            _baseUrl.Contains("crownanalyzer.com/backend", StringComparison.OrdinalIgnoreCase))
        {
            _baseUrl = GetBackendUrl();
            Preferences.Set(GetBaseUrlKey(), _baseUrl);
        }
        
        // Configurar HttpClient con mejor manejo de conexiones para iOS
#if IOS || MACCATALYST
        var handler = new NSUrlSessionHandler
        {
            // Permitir conexiones inseguras en desarrollo (HTTP en lugar de HTTPS)
            TrustOverrideForUrl = (sender, url, trust) => 
            {
                var host = new NSUrl(url).Host;
                return host == "backend.crownanalyzer.com";
            }
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60) // Timeout más largo para redes móviles
        };
#else
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
#endif

        // Restaurar sesión si existe
        RestoreSession();
    }

        private static string GetBackendUrl()
        {
        // Backend en VPS (subdominio dedicado)
        return "https://backend.crownanalyzer.com/api";
        }

    public void UpdateBaseUrl(string baseUrl)
    {
        var normalized = NormalizeBaseUrl(baseUrl);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        _baseUrl = normalized;
        Preferences.Set(GetBaseUrlKey(), _baseUrl);
        System.Diagnostics.Debug.WriteLine($"[CloudBackend] BaseUrl actualizado: {_baseUrl}");
    }

        private static string GetBaseUrlKey()
        {
    #if IOS
        return $"{BaseUrlKeyPrefix}_iOS";
    #elif MACCATALYST
        return $"{BaseUrlKeyPrefix}_MacCatalyst";
    #elif ANDROID
        return $"{BaseUrlKeyPrefix}_Android";
    #elif WINDOWS
        return $"{BaseUrlKeyPrefix}_Windows";
    #else
        return BaseUrlKeyPrefix;
    #endif
        }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return string.Empty;

        var url = baseUrl.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "http://" + url;
        }

        // Asegurar que termine en /api
        if (!url.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            url = url.TrimEnd('/') + "/api";
        }

        return url;
    }

    private void RestoreSession()
    {
        try
        {
            _accessToken = Preferences.Get(AccessTokenKey, string.Empty);
            _refreshToken = Preferences.Get(RefreshTokenKey, string.Empty);
            CurrentUserName = Preferences.Get(UserNameKey, string.Empty);
            TeamName = Preferences.Get(TeamNameKey, string.Empty);
            CurrentUserRole = Preferences.Get(UserRoleKey, string.Empty);

            var expiresAtStr = Preferences.Get(TokenExpiresAtKey, string.Empty);
            if (!string.IsNullOrEmpty(expiresAtStr) && DateTime.TryParse(expiresAtStr, out var expiresAt))
            {
                _tokenExpiresAt = expiresAt;
            }

            if (!string.IsNullOrEmpty(_accessToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new AuthenticationHeaderValue("Bearer", _accessToken);
                System.Diagnostics.Debug.WriteLine($"[CloudBackend] Sesión restaurada para {CurrentUserName}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] Error restaurando sesión: {ex.Message}");
        }
    }

    private void SaveSession()
    {
        try
        {
            Preferences.Set(AccessTokenKey, _accessToken ?? string.Empty);
            Preferences.Set(RefreshTokenKey, _refreshToken ?? string.Empty);
            Preferences.Set(UserNameKey, CurrentUserName ?? string.Empty);
            Preferences.Set(TeamNameKey, TeamName ?? string.Empty);
            Preferences.Set(UserRoleKey, CurrentUserRole ?? string.Empty);
            Preferences.Set(TokenExpiresAtKey, _tokenExpiresAt?.ToString("O") ?? string.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] Error guardando sesión: {ex.Message}");
        }
    }

    private void ClearSession()
    {
        _accessToken = null;
        _refreshToken = null;
        _tokenExpiresAt = null;
        CurrentUserName = null;
        TeamName = null;
        CurrentUserRole = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;

        try
        {
            Preferences.Remove(AccessTokenKey);
            Preferences.Remove(RefreshTokenKey);
            Preferences.Remove(UserNameKey);
            Preferences.Remove(TeamNameKey);
            Preferences.Remove(UserRoleKey);
            Preferences.Remove(TokenExpiresAtKey);
        }
        catch { }
    }

    private static string GetPlatformName()
    {
        var platform = DeviceInfo.Platform;
        if (platform == DevicePlatform.iOS)
            return "iOS";
        if (platform == DevicePlatform.MacCatalyst)
            return "MacCatalyst";
        if (platform == DevicePlatform.macOS)
            return "macOS";
        if (platform == DevicePlatform.WinUI)
            return "Windows";
        if (platform == DevicePlatform.Android)
            return "Android";

        return platform.ToString();
    }

    private static async Task<string> GetOrCreateDeviceIdAsync()
    {
        try
        {
            var stored = await SecureStorage.GetAsync(DeviceIdKey);
            if (!string.IsNullOrWhiteSpace(stored))
                return stored;
        }
        catch { }

        var fallback = Preferences.Get(DeviceIdKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;

        var deviceId = Guid.NewGuid().ToString();
        try
        {
            await SecureStorage.SetAsync(DeviceIdKey, deviceId);
        }
        catch
        {
            Preferences.Set(DeviceIdKey, deviceId);
        }

        return deviceId;
    }

    /// <summary>
    /// Ejecuta una petición HTTP con reintentos automáticos para manejar conexiones perdidas.
    /// </summary>
    private async Task<HttpResponseMessage> ExecuteWithRetryAsync(Func<Task<HttpResponseMessage>> request, int maxRetries = 3)
    {
        Exception? lastException = null;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var response = await request();
                return response;
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                AppLog.Warn("CloudBackend", $"Intento {attempt}/{maxRetries} falló: {ex.Message}");
                
                if (attempt < maxRetries)
                {
                    // Esperar con backoff exponencial antes de reintentar
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                    await Task.Delay(delay);
                }
            }
            catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
            {
                // Timeout - reintentar
                lastException = ex;
                AppLog.Warn("CloudBackend", $"Timeout en intento {attempt}/{maxRetries}");
                
                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }
        }
        
        throw lastException ?? new HttpRequestException("Error de conexión después de múltiples reintentos");
    }

    public async Task<AuthResult> LoginAsync(string email, string password)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] Iniciando login para {email}...");

            var deviceId = await GetOrCreateDeviceIdAsync();
            var devicePlatform = GetPlatformName();
            var deviceName = DeviceInfo.Name;

            var requestBody = new
            {
                email,
                password,
                deviceId,
                devicePlatform,
                deviceName
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync($"{_baseUrl}/auth/login", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var errorResult = TryParseError(responseBody);
                return new AuthResult(false, errorResult ?? $"Error de autenticación: {response.StatusCode}");
            }

            var loginResponse = JsonSerializer.Deserialize<LoginResponseDto>(responseBody, JsonOptions);
            if (loginResponse == null)
            {
                return new AuthResult(false, "Respuesta inválida del servidor");
            }

            // Guardar tokens
            _accessToken = loginResponse.AccessToken;
            _refreshToken = loginResponse.RefreshToken;
            _tokenExpiresAt = DateTime.UtcNow.AddSeconds(loginResponse.ExpiresIn);
            CurrentUserName = loginResponse.User?.Name ?? email;
            TeamName = loginResponse.User?.TeamName ?? "Equipo";
            CurrentUserRole = loginResponse.User?.Role ?? string.Empty;

            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _accessToken);

            SaveSession();

            System.Diagnostics.Debug.WriteLine($"[CloudBackend] Login exitoso para {CurrentUserName} en {TeamName}");

            return new AuthResult(
                true,
                null,
                CurrentUserName,
                TeamName,
                _accessToken,
                _tokenExpiresAt,
                CurrentUserRole
            );
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] Error de red: {ex.Message}");
            return new AuthResult(false, "No se pudo conectar al servidor. Verifica tu conexión a internet.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] Error: {ex.Message}");
            return new AuthResult(false, $"Error inesperado: {ex.Message}");
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(_accessToken))
            {
                // Notificar al backend (opcional, el token expirará de todos modos)
                await _httpClient.PostAsync($"{_baseUrl}/auth/logout", null);
            }
        }
        catch { }
        finally
        {
            ClearSession();
            System.Diagnostics.Debug.WriteLine("[CloudBackend] Sesión cerrada");
        }
    }

    public async Task<bool> RefreshTokenIfNeededAsync()
    {
        // Si el token expira en menos de 5 minutos, refrescarlo
        if (_tokenExpiresAt.HasValue && _tokenExpiresAt.Value > DateTime.UtcNow.AddMinutes(5))
        {
            return true; // Token aún válido
        }

        if (string.IsNullOrEmpty(_refreshToken))
        {
            return false; // No hay refresh token
        }

        try
        {
            var requestBody = new { refreshToken = _refreshToken };
            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync($"{_baseUrl}/auth/refresh", content);
            if (!response.IsSuccessStatusCode)
            {
                ClearSession();
                return false;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            var refreshResponse = JsonSerializer.Deserialize<LoginResponseDto>(responseBody, JsonOptions);
            
            if (refreshResponse == null)
            {
                ClearSession();
                return false;
            }

            _accessToken = refreshResponse.AccessToken;
            _refreshToken = refreshResponse.RefreshToken;
            _tokenExpiresAt = DateTime.UtcNow.AddSeconds(refreshResponse.ExpiresIn);

            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _accessToken);

            SaveSession();
            return true;
        }
        catch
        {
            ClearSession();
            return false;
        }
    }

    public async Task<CloudFileListResult> ListFilesAsync(string folderPath = "", int maxItems = 100, string? continuationToken = null)
    {
        if (!await EnsureAuthenticatedAsync())
        {
            return new CloudFileListResult(false, "No autenticado");
        }

        try
        {
            var url = $"{_baseUrl}/files/list?path={Uri.EscapeDataString(folderPath)}&max={maxItems}";
            if (!string.IsNullOrEmpty(continuationToken))
            {
                url += $"&token={Uri.EscapeDataString(continuationToken)}";
            }

            var response = await ExecuteWithRetryAsync(() => _httpClient.GetAsync(url));
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new CloudFileListResult(false, TryParseError(responseBody) ?? "Error al listar archivos");
            }

            var listResponse = JsonSerializer.Deserialize<FileListResponseDto>(responseBody, JsonOptions);
            if (listResponse == null)
            {
                return new CloudFileListResult(false, "Respuesta inválida");
            }

            var files = listResponse.Files?.ConvertAll(f => new CloudFileInfo(
                f.Key ?? "",
                f.Name ?? "",
                f.Size,
                f.LastModified,
                f.IsFolder,
                f.ContentType,
                f.ThumbnailUrl
            )) ?? new List<CloudFileInfo>();

            return new CloudFileListResult(
                true,
                null,
                files,
                listResponse.ContinuationToken,
                listResponse.HasMore
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] Error listando archivos: {ex.Message}");
            return new CloudFileListResult(false, $"Error: {ex.Message}");
        }
    }

    public async Task<PresignedUrlResult> GetDownloadUrlAsync(string filePath, int expirationMinutes = 60)
    {
        if (!await EnsureAuthenticatedAsync())
        {
            return new PresignedUrlResult(false, "No autenticado");
        }

        try
        {
            var requestBody = new
            {
                path = filePath,
                expirationMinutes
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await ExecuteWithRetryAsync(() => _httpClient.PostAsync($"{_baseUrl}/files/download-url", content));
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new PresignedUrlResult(false, TryParseError(responseBody) ?? "Error al obtener URL");
            }

            var urlResponse = JsonSerializer.Deserialize<PresignedUrlResponseDto>(responseBody, JsonOptions);
            if (urlResponse == null)
            {
                return new PresignedUrlResult(false, "Respuesta inválida");
            }

            return new PresignedUrlResult(
                true,
                null,
                urlResponse.Url,
                DateTime.UtcNow.AddMinutes(expirationMinutes)
            );
        }
        catch (Exception ex)
        {
            return new PresignedUrlResult(false, $"Error: {ex.Message}");
        }
    }

    public async Task<PresignedUrlResult> GetUploadUrlAsync(string filePath, string contentType, int expirationMinutes = 60)
    {
        if (!await EnsureAuthenticatedAsync())
        {
            return new PresignedUrlResult(false, "No autenticado");
        }

        try
        {
            var requestBody = new
            {
                path = filePath,
                contentType,
                expirationMinutes
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await ExecuteWithRetryAsync(() => _httpClient.PostAsync($"{_baseUrl}/files/upload-url", content));
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new PresignedUrlResult(false, TryParseError(responseBody) ?? "Error al obtener URL de subida");
            }

            var urlResponse = JsonSerializer.Deserialize<PresignedUrlResponseDto>(responseBody, JsonOptions);
            if (urlResponse == null)
            {
                return new PresignedUrlResult(false, "Respuesta inválida");
            }

            return new PresignedUrlResult(
                true,
                null,
                urlResponse.Url,
                DateTime.UtcNow.AddMinutes(expirationMinutes),
                urlResponse.Headers
            );
        }
        catch (Exception ex)
        {
            return new PresignedUrlResult(false, $"Error: {ex.Message}");
        }
    }

    public async Task<bool> ConfirmUploadAsync(string filePath, long fileSize)
    {
        if (!await EnsureAuthenticatedAsync())
        {
            return false;
        }

        try
        {
            var requestBody = new
            {
                path = filePath,
                size = fileSize
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await ExecuteWithRetryAsync(() => _httpClient.PostAsync($"{_baseUrl}/files/confirm-upload", content));
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteFileAsync(string filePath)
    {
        if (!await EnsureAuthenticatedAsync())
        {
            System.Diagnostics.Debug.WriteLine("[CloudBackend] DeleteFileAsync: No autenticado");
            return false;
        }

        // Validación de seguridad: solo eliminar archivos con extensión válida
        if (string.IsNullOrWhiteSpace(filePath))
        {
            System.Diagnostics.Debug.WriteLine("[CloudBackend] DeleteFileAsync: BLOQUEADO - path vacío");
            return false;
        }

        var validExtensions = new[] { ".mp4", ".jpg", ".jpeg", ".png", ".json", ".crown", ".mov", ".avi", ".webm" };
        var hasValidExtension = validExtensions.Any(ext => filePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        
        if (!hasValidExtension)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] DeleteFileAsync: BLOQUEADO - sin extensión válida: {filePath}");
            return false;
        }

        // El path debe tener estructura de carpetas mínima
        if (!filePath.Contains('/') || filePath.Split('/').Length < 3)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] DeleteFileAsync: BLOQUEADO - path sin estructura válida: {filePath}");
            return false;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] DeleteFileAsync: Enviando DELETE para path={filePath}");
            
            var requestBody = new { path = filePath };
            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await ExecuteWithRetryAsync(() => _httpClient.PostAsync($"{_baseUrl}/files/delete", content));
            var responseBody = await response.Content.ReadAsStringAsync();
            
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] DeleteFileAsync: StatusCode={response.StatusCode}, Body={responseBody}");
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] DeleteFileAsync: Exception={ex.Message}");
            return false;
        }
    }

    public async Task<TeamInfoResult> GetTeamInfoAsync()
    {
        if (!await EnsureAuthenticatedAsync())
        {
            return new TeamInfoResult(false, "No autenticado");
        }

        try
        {
            var response = await ExecuteWithRetryAsync(() => _httpClient.GetAsync($"{_baseUrl}/team/info"));
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new TeamInfoResult(false, TryParseError(responseBody) ?? "Error al obtener info del equipo");
            }

            var teamResponse = JsonSerializer.Deserialize<TeamInfoResponseDto>(responseBody, JsonOptions);
            if (teamResponse == null)
            {
                return new TeamInfoResult(false, "Respuesta inválida");
            }

            var members = teamResponse.Members?.ConvertAll(m => new TeamMemberInfo(
                m.Id ?? "",
                m.Name ?? "",
                m.Email ?? "",
                m.Role ?? "member"
            ));

            return new TeamInfoResult(
                true,
                null,
                teamResponse.Name,
                teamResponse.StorageUsed,
                teamResponse.StorageLimit,
                teamResponse.TotalFiles,
                members
            );
        }
        catch (Exception ex)
        {
            return new TeamInfoResult(false, $"Error: {ex.Message}");
        }
    }

    public async Task<BackendHealthResult> CheckHealthAsync()
    {
        try
        {
            // El endpoint /health no requiere autenticación
            var baseUrlWithoutApi = _baseUrl.Replace("/api", "");
            var response = await _httpClient.GetAsync($"{baseUrlWithoutApi}/health");
            
            if (!response.IsSuccessStatusCode)
            {
                return new BackendHealthResult(false, $"Backend no disponible: {response.StatusCode}");
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            var healthResponse = JsonSerializer.Deserialize<HealthResponseDto>(responseBody, JsonOptions);

            System.Diagnostics.Debug.WriteLine($"[CloudBackend] Health check OK: status={healthResponse?.Status}, version={healthResponse?.Version}");

            return new BackendHealthResult(
                true,
                null,
                healthResponse?.Version,
                DateTime.UtcNow
            );
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] Health check falló - backend no accesible: {ex.Message}");
            return new BackendHealthResult(false, $"Backend no accesible: {ex.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] Health check error: {ex.Message}");
            return new BackendHealthResult(false, $"Error: {ex.Message}");
        }
    }

    public async Task<GallerySyncResult> CheckForGalleryUpdatesAsync(DateTime? lastSyncTime = null)
    {
        if (!await EnsureAuthenticatedAsync())
        {
            return new GallerySyncResult(false, "No autenticado");
        }

        try
        {
            // Listar todos los archivos del equipo
            var allFiles = new List<CloudFileInfo>();
            string? continuationToken = null;
            
            do
            {
                var result = await ListFilesAsync("", 500, continuationToken);
                if (!result.Success)
                {
                    return new GallerySyncResult(false, result.ErrorMessage);
                }
                
                if (result.Files != null)
                {
                    allFiles.AddRange(result.Files);
                }
                
                continuationToken = result.ContinuationToken;
            }
            while (!string.IsNullOrEmpty(continuationToken));

            // Filtrar archivos nuevos/actualizados desde la última sincronización
            var newFiles = new List<CloudFileInfo>();
            var updatedFiles = new List<CloudFileInfo>();

            foreach (var file in allFiles)
            {
                if (file.IsFolder) continue;
                
                if (lastSyncTime.HasValue)
                {
                    if (file.LastModified > lastSyncTime.Value)
                    {
                        // Es un archivo nuevo o actualizado
                        updatedFiles.Add(file);
                    }
                }
                else
                {
                    // Primera sincronización - todos son nuevos
                    newFiles.Add(file);
                }
            }

            System.Diagnostics.Debug.WriteLine($"[CloudBackend] Gallery sync: {newFiles.Count} nuevos, {updatedFiles.Count} actualizados de {allFiles.Count} total");

            return new GallerySyncResult(
                true,
                null,
                newFiles.Count,
                updatedFiles.Count,
                newFiles,
                updatedFiles,
                DateTime.UtcNow
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] Error checking gallery updates: {ex.Message}");
            return new GallerySyncResult(false, $"Error: {ex.Message}");
        }
    }

    private async Task<bool> EnsureAuthenticatedAsync()
    {
        if (!IsAuthenticated)
        {
            return await RefreshTokenIfNeededAsync();
        }
        return true;
    }

    private static string? TryParseError(string responseBody)
    {
        try
        {
            var errorDoc = JsonDocument.Parse(responseBody);
            if (errorDoc.RootElement.TryGetProperty("error", out var errorProp))
            {
                return errorProp.GetString();
            }
            if (errorDoc.RootElement.TryGetProperty("message", out var msgProp))
            {
                return msgProp.GetString();
            }
        }
        catch { }
        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // DTOs para deserialización
    private class LoginResponseDto
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
        public UserDto? User { get; set; }
    }

    private class UserDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? TeamId { get; set; }
        public string? TeamName { get; set; }
        public string? Role { get; set; }
    }

    private class FileListResponseDto
    {
        public List<FileDto>? Files { get; set; }
        public string? ContinuationToken { get; set; }
        public bool HasMore { get; set; }
    }

    private class FileDto
    {
        public string? Key { get; set; }
        public string? Name { get; set; }
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public bool IsFolder { get; set; }
        public string? ContentType { get; set; }
        public string? ThumbnailUrl { get; set; }
    }

    private class PresignedUrlResponseDto
    {
        public string? Url { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
    }

    private class TeamInfoResponseDto
    {
        public string? Name { get; set; }
        public long StorageUsed { get; set; }
        public long StorageLimit { get; set; }
        public int TotalFiles { get; set; }
        public List<TeamMemberDto>? Members { get; set; }
    }

    private class TeamMemberDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
    }

    private class HealthResponseDto
    {
        public string? Status { get; set; }
        public string? Version { get; set; }
        public string? Timestamp { get; set; }
    }
}
