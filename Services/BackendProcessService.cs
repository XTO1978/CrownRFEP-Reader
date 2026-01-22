using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Servicio que gestiona el proceso del backend de Node.js en desktop (Mac/Windows).
/// Inicia el backend automáticamente y lo mantiene corriendo mientras la app esté abierta.
/// </summary>
public class BackendProcessService : IDisposable
{
    private Process? _backendProcess;
    private bool _disposed;
    private readonly string _backendPath;
    private readonly int _port;
    
    public bool IsRunning => _backendProcess != null && !_backendProcess.HasExited;
    public int Port => _port;

    public BackendProcessService()
    {
        _port = 3000;
        
        // Determinar la ruta del backend
        // En desarrollo, está en el directorio del proyecto
        // En producción, podría estar en el bundle de la app
        var currentDir = AppContext.BaseDirectory;
        
        // Buscar el directorio backend subiendo desde el directorio de ejecución
        var searchDir = currentDir;
        for (int i = 0; i < 10; i++)
        {
            var possibleBackendPath = Path.Combine(searchDir, "backend");
            if (Directory.Exists(possibleBackendPath) && 
                File.Exists(Path.Combine(possibleBackendPath, "src", "index.js")))
            {
                _backendPath = possibleBackendPath;
                break;
            }
            
            var parent = Directory.GetParent(searchDir);
            if (parent == null) break;
            searchDir = parent.FullName;
        }
        
        // Ruta alternativa para desarrollo
        if (string.IsNullOrEmpty(_backendPath))
        {
            var devPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "CrownRFEP Reader", "CrownRFEP-Reader", "backend");
            if (Directory.Exists(devPath))
            {
                _backendPath = devPath;
            }
        }
        
        AppLog.Info("BackendProcess", $"Backend path: {_backendPath}");
    }

    /// <summary>
    /// Inicia el proceso del backend si no está corriendo.
    /// </summary>
    public async Task<bool> StartAsync()
    {
        if (IsRunning)
        {
            AppLog.Info("BackendProcess", "Backend ya está corriendo");
            return true;
        }

        // Verificar si ya hay un backend corriendo en el puerto
        if (await IsPortInUseAsync())
        {
            AppLog.Info("BackendProcess", $"Puerto {_port} ya está en uso, asumiendo backend externo");
            return true;
        }

        if (string.IsNullOrEmpty(_backendPath) || !Directory.Exists(_backendPath))
        {
            AppLog.Warn("BackendProcess", $"Directorio del backend no encontrado: {_backendPath}");
            return false;
        }

        try
        {
            // Buscar node en las rutas comunes
            var nodePath = FindNodePath();
            if (string.IsNullOrEmpty(nodePath))
            {
                AppLog.Error("BackendProcess", "Node.js no encontrado en el sistema");
                return false;
            }

            var indexPath = Path.Combine(_backendPath, "src", "index.js");
            if (!File.Exists(indexPath))
            {
                AppLog.Error("BackendProcess", $"Archivo index.js no encontrado: {indexPath}");
                return false;
            }

            AppLog.Info("BackendProcess", $"Iniciando backend: {nodePath} {indexPath}");

            var startInfo = new ProcessStartInfo
            {
                FileName = nodePath,
                Arguments = indexPath,
                WorkingDirectory = _backendPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Environment = 
                {
                    ["NODE_ENV"] = "development",
                    ["PORT"] = _port.ToString()
                }
            };

            // Copiar variables de entorno del .env si existe
            var envFile = Path.Combine(_backendPath, ".env");
            if (File.Exists(envFile))
            {
                foreach (var line in File.ReadAllLines(envFile))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                        continue;

                    var parts = trimmed.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim().Trim('"');
                        startInfo.Environment[key] = value;
                    }
                }
            }

            _backendProcess = new Process { StartInfo = startInfo };
            
            _backendProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    AppLog.Info("Backend", e.Data);
                }
            };
            
            _backendProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    AppLog.Warn("Backend", e.Data);
                }
            };

            _backendProcess.Start();
            _backendProcess.BeginOutputReadLine();
            _backendProcess.BeginErrorReadLine();

            // Esperar a que el backend esté listo
            var ready = await WaitForBackendReadyAsync(timeoutSeconds: 10);
            
            if (ready)
            {
                AppLog.Info("BackendProcess", $"✅ Backend iniciado correctamente en puerto {_port}");
            }
            else
            {
                AppLog.Warn("BackendProcess", "Backend iniciado pero no responde al health check");
            }

            return ready;
        }
        catch (Exception ex)
        {
            AppLog.Error("BackendProcess", "Error iniciando backend", ex);
            return false;
        }
    }

    /// <summary>
    /// Detiene el proceso del backend.
    /// </summary>
    public void Stop()
    {
        if (_backendProcess == null) return;

        try
        {
            if (!_backendProcess.HasExited)
            {
                AppLog.Info("BackendProcess", "Deteniendo backend...");
                _backendProcess.Kill(entireProcessTree: true);
                _backendProcess.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("BackendProcess", $"Error deteniendo backend: {ex.Message}");
        }
        finally
        {
            _backendProcess.Dispose();
            _backendProcess = null;
        }
    }

    private string? FindNodePath()
    {
#if WINDOWS
        // Rutas comunes de Node.js en Windows
        var possiblePaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming", "nvm", "v18.20.8", "node.exe"),
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Intentar encontrar con where en Windows
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c where node",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process != null)
            {
                var result = process.StandardOutput.ReadLine()?.Trim();
                process.WaitForExit();
                if (!string.IsNullOrEmpty(result) && File.Exists(result))
                {
                    return result;
                }
            }
        }
        catch { }
#else
        // Rutas comunes de Node.js en macOS
        var possiblePaths = new[]
        {
            "/usr/local/bin/node",
            "/opt/homebrew/bin/node",
            "/usr/bin/node",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nvm/versions/node/v18.20.8/bin/node"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nvm/versions/node/v20.10.0/bin/node"),
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Intentar encontrar con which en macOS/Linux
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = "-c \"which node\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process != null)
            {
                var result = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                if (!string.IsNullOrEmpty(result) && File.Exists(result))
                {
                    return result;
                }
            }
        }
        catch { }
#endif

        return null;
    }

    private async Task<bool> IsPortInUseAsync()
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await client.GetAsync($"http://localhost:{_port}/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> WaitForBackendReadyAsync(int timeoutSeconds)
    {
        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var endTime = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < endTime)
        {
            try
            {
                var response = await client.GetAsync($"http://localhost:{_port}/health");
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
                // Ignorar errores de conexión mientras esperamos
            }

            await Task.Delay(500);
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
