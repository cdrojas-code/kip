using System.Text;

namespace StickyNotes.App.Logging;

/// <summary>Registro sencillo en %LOCALAPPDATA%\Kip\logs\app-yyyyMMdd.log.
/// Nunca lanza excepciones: un fallo de log no debe tumbar la aplicación.</summary>
public sealed class FileLogger
{
    private readonly object _gate = new();
    private readonly string _directory;

    public FileLogger(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kip", "logs");
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private void Write(string level, string message, Exception? ex)
    {
        try
        {
            var now = DateTime.Now;
            var sb = new StringBuilder()
                .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)).Append(" [").Append(level).Append("] ")
                .Append(message);
            if (ex is not null) sb.AppendLine().Append(ex);
            sb.AppendLine();

            lock (_gate)
            {
                Directory.CreateDirectory(_directory);
                File.AppendAllText(Path.Combine(_directory, $"app-{now:yyyyMMdd}.log"), sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Ignorado a propósito: ver comentario de la clase.
        }
    }
}
