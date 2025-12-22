using System.Diagnostics;

namespace CrownRFEP_Reader.Services;

public static class AppLog
{
    public static void Info(string area, string message)
        => Write("INFO", area, message, null);

    public static void Warn(string area, string message)
        => Write("WARN", area, message, null);

    public static void Error(string area, string message, Exception? ex = null)
        => Write("ERROR", area, message, ex);

    private static void Write(string level, string area, string message, Exception? ex)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss.fff");
        var threadId = Environment.CurrentManagedThreadId;
        var line = $"[{ts}] [{level}] [{area}] [T{threadId}] {message}";

        if (ex != null)
            line += $"\n{ex}";

        Debug.WriteLine(line);
        try
        {
            Console.WriteLine(line);
        }
        catch
        {
            // Ignorar: consola no disponible en algunos contextos.
        }
    }
}
