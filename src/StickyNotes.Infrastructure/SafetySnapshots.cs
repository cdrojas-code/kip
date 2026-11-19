using System.Globalization;

namespace StickyNotes.Infrastructure;

/// <summary>
/// Instantáneas de seguridad rotativas: <c>backups\notes-safety-yyyyMMdd-HHmmss-fff.json</c>.
/// Se crean antes de operaciones de riesgo (importar, borrado masivo) y se conservan las <see cref="Keep"/> más recientes.
/// No siguen la convención de los backups diarios, así que la poda diaria y el fallback de carga las ignoran.
/// </summary>
internal static class SafetySnapshots
{
    public const int Keep = 10;
    private const string Prefix = "notes-safety-";
    private const string Extension = ".json";

    /// <summary>Copia un archivo existente como instantánea.</summary>
    public static void CopyFrom(string sourceFile, string backupDirectory)
    {
        Directory.CreateDirectory(backupDirectory);
        var target = NewPath(backupDirectory);
        File.Copy(sourceFile, target);
        // File.Copy conserva la fecha del origen; la rotación se basa en cuándo se creó la instantánea.
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
        Prune(backupDirectory);
    }

    /// <summary>Escribe un contenido como instantánea.</summary>
    public static async Task WriteAsync(ReadOnlyMemory<byte> content, string backupDirectory, CancellationToken ct)
    {
        await AtomicFile.WriteAllBytesAsync(NewPath(backupDirectory), content, backupPath: null, ct).ConfigureAwait(false);
        Prune(backupDirectory);
    }

    public static IReadOnlyList<string> ListNewestFirst(string backupDirectory) =>
        Directory.Exists(backupDirectory)
            ? [.. new DirectoryInfo(backupDirectory).EnumerateFiles(Prefix + "*" + Extension)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ThenByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => f.FullName)]
            : [];

    private static string NewPath(string backupDirectory)
    {
        Directory.CreateDirectory(backupDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var path = Path.Combine(backupDirectory, Prefix + stamp + Extension);
        for (var i = 1; File.Exists(path); i++)
        {
            path = Path.Combine(backupDirectory, $"{Prefix}{stamp}-{i}{Extension}");
        }

        return path;
    }

    private static void Prune(string backupDirectory)
    {
        foreach (var old in ListNewestFirst(backupDirectory).Skip(Keep))
        {
            try
            {
                File.Delete(old);
            }
            catch (IOException)
            {
                // Best-effort: se intentará en la próxima instantánea.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
