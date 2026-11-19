using System.Diagnostics;
using StickyNotes.Core.Abstractions;

namespace StickyNotes.Infrastructure;

/// <summary>Rutas de datos locales de la aplicación. Crea los directorios al construirse.</summary>
public sealed class AppPaths : IAppPaths
{
    /// <summary>Carpeta de datos con el nombre anterior de la app (StickyNotes).</summary>
    internal const string LegacyFolderName = "StickyNotes";
    internal const string FolderName = "Kip";

    /// <summary>Usa %LOCALAPPDATA%\Kip (migrando antes los datos de %LOCALAPPDATA%\StickyNotes si existen).</summary>
    public AppPaths()
        : this(PrepareDefaultRoot())
    {
    }

    /// <summary>Usa un directorio raíz arbitrario (pruebas).</summary>
    public AppPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        DataDirectory = Path.GetFullPath(root);
        NotesFile = Path.Combine(DataDirectory, "notes.json");
        SettingsFile = Path.Combine(DataDirectory, "settings.json");
        BackupDirectory = Path.Combine(DataDirectory, "backups");
        LogDirectory = Path.Combine(DataDirectory, "logs");

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public string DataDirectory { get; }

    public string NotesFile { get; }

    public string SettingsFile { get; }

    public string BackupDirectory { get; }

    public string LogDirectory { get; }

    private static string PrepareDefaultRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(localAppData, FolderName);
        MigrateLegacyData(Path.Combine(localAppData, LegacyFolderName), root);
        return root;
    }

    /// <summary>
    /// Mueve los datos de la carpeta antigua a la nueva sólo si la nueva aún no tiene notas
    /// (la carpeta nueva puede existir ya porque el logger crea "logs" antes).
    /// Nunca sobrescribe ni borra datos: lo que ya exista en destino se deja en origen.
    /// </summary>
    internal static void MigrateLegacyData(string legacyRoot, string newRoot)
    {
        try
        {
            if (!Directory.Exists(legacyRoot) || File.Exists(Path.Combine(newRoot, "notes.json"))) return;

            Directory.CreateDirectory(newRoot);
            foreach (var file in Directory.GetFiles(legacyRoot))
            {
                var target = Path.Combine(newRoot, Path.GetFileName(file));
                if (!File.Exists(target)) File.Move(file, target);
            }
            foreach (var dir in Directory.GetDirectories(legacyRoot))
            {
                var target = Path.Combine(newRoot, Path.GetFileName(dir));
                if (!Directory.Exists(target)) { Directory.Move(dir, target); continue; }
                foreach (var file in Directory.GetFiles(dir))
                {
                    var fileTarget = Path.Combine(target, Path.GetFileName(file));
                    if (!File.Exists(fileTarget)) File.Move(file, fileTarget);
                }
            }
            Trace.TraceInformation($"Datos migrados de {legacyRoot} a {newRoot}.");
        }
        catch (Exception ex)
        {
            // La migración es de cortesía: si falla, la app arranca igual y los datos antiguos quedan intactos.
            Trace.TraceError($"AppPaths.MigrateLegacyData: {ex}");
        }
    }
}
