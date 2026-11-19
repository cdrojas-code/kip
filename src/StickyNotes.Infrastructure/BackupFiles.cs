using System.Globalization;

namespace StickyNotes.Infrastructure;

/// <summary>Convención de nombres de los backups diarios: <c>notes-yyyyMMdd.json</c>.</summary>
internal static class BackupFiles
{
    private const string Prefix = "notes-";
    private const string Extension = ".json";
    private const string DateFormat = "yyyyMMdd";

    public static string FileNameFor(DateOnly date) =>
        Prefix + date.ToString(DateFormat, CultureInfo.InvariantCulture) + Extension;

    /// <summary>Extrae la fecha del nombre; false si el archivo no sigue la convención.</summary>
    public static bool TryGetDate(string path, out DateOnly date)
    {
        var name = Path.GetFileName(path);
        date = default;
        return name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
            && DateOnly.TryParseExact(
                name[Prefix.Length..^Extension.Length], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>Backups válidos del directorio, con su fecha, del más reciente al más antiguo.</summary>
    public static IEnumerable<(string Path, DateOnly Date)> ListWithDates(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, Prefix + "*" + Extension)
            .Select(p => (Ok: TryGetDate(p, out var d), Path: p, Date: d))
            .Where(x => x.Ok)
            .OrderByDescending(x => x.Date)
            .Select(x => (x.Path, x.Date))
            .ToList();
    }

    public static IEnumerable<string> ListNewestFirst(string directory) =>
        ListWithDates(directory).Select(x => x.Path);
}
