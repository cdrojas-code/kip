using System.Text.Json;
using System.Text.Json.Serialization;

namespace StickyNotes.Infrastructure;

/// <summary>Opciones de serialización compartidas por repositorio, ajustes y backups.</summary>
internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
