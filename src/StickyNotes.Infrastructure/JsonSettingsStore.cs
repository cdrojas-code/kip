using System.Text.Json;
using StickyNotes.Core.Abstractions;
using StickyNotes.Core.Models;

namespace StickyNotes.Infrastructure;

/// <summary>
/// Persiste los ajustes en settings.json con escritura atómica.
/// Inexistente o corrupto = valores por defecto; valores fuera de rango se corrigen (<see cref="AppSettings.Normalize"/>).
/// Bloqueos transitorios se reintentan; si persisten, se lanza la excepción.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly IAppPaths _paths;
    private readonly IoRetry _retry;

    public JsonSettingsStore(IAppPaths paths)
        : this(paths, IoRetry.Default)
    {
    }

    internal JsonSettingsStore(IAppPaths paths, IoRetry retry)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(retry);
        _paths = paths;
        _retry = retry;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_paths.SettingsFile))
        {
            return new AppSettings();
        }

        try
        {
            var settings = await _retry.RunAsync(() => ReadAsync(ct), ct).ConfigureAwait(false);
            return (settings ?? new AppSettings()).Normalize();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonDefaults.Options);
        return _retry.RunAsync(
            () => AtomicFile.WriteAllBytesAsync(_paths.SettingsFile, bytes, _paths.SettingsFile + ".bak", ct), ct);
    }

    private async Task<AppSettings?> ReadAsync(CancellationToken ct)
    {
        var stream = new FileStream(_paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonDefaults.Options, ct).ConfigureAwait(false);
        }
    }
}
