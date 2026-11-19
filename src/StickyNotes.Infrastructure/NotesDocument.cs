using System.Text.Json;
using StickyNotes.Core.Models;

namespace StickyNotes.Infrastructure;

/// <summary>
/// Formato en disco de las notas: <c>{ "schemaVersion": 1, "notes": [ ... ] }</c>.
/// Lo comparten notes.json, los backups diarios y las exportaciones.
/// </summary>
internal sealed class NotesDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<Note>? Notes { get; set; }

    public static byte[] Serialize(IReadOnlyCollection<Note> notes)
    {
        var document = new NotesDocument { Notes = [.. notes] };
        return JsonSerializer.SerializeToUtf8Bytes(document, JsonDefaults.Options);
    }

    /// <summary>Lee y valida un archivo de notas.</summary>
    /// <exception cref="InvalidDataException">El contenido no es un documento de notas válido.</exception>
    public static async Task<IReadOnlyList<Note>> ReadAsync(string path, CancellationToken ct)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            NotesDocument? document;
            try
            {
                document = await JsonSerializer.DeserializeAsync<NotesDocument>(stream, JsonDefaults.Options, ct).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"El archivo '{Path.GetFileName(path)}' no contiene JSON de notas válido.", ex);
            }

            return Validate(document, path);
        }
    }

    private static List<Note> Validate(NotesDocument? document, string path)
    {
        var name = Path.GetFileName(path);
        if (document?.Notes is null)
        {
            throw new InvalidDataException($"El archivo '{name}' no tiene la lista de notas esperada.");
        }

        if (document.SchemaVersion < 1 || document.SchemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidDataException($"El archivo '{name}' usa una versión de formato no soportada ({document.SchemaVersion}).");
        }

        if (document.Notes.Exists(n => n is null || n.Id == Guid.Empty))
        {
            throw new InvalidDataException($"El archivo '{name}' contiene notas inválidas.");
        }

        // Tolerar listas nulas escritas a mano para que el resto del código no tenga que comprobarlas.
        foreach (var note in document.Notes)
        {
            note.Checklist ??= [];
            note.Title ??= string.Empty;
            note.RichContent ??= string.Empty;
            note.PlainText ??= string.Empty;
        }

        return document.Notes;
    }
}
