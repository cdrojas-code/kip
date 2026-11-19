namespace StickyNotes.Infrastructure;

/// <summary>
/// Escritura atómica: se escribe un temporal, se fuerza a disco y se sustituye el destino.
/// Si el PC se apaga a mitad, el archivo original queda intacto.
/// </summary>
internal static class AtomicFile
{
    /// <param name="path">Archivo destino.</param>
    /// <param name="content">Bytes a escribir.</param>
    /// <param name="backupPath">Dónde conservar la versión anterior (null = no conservar).</param>
    public static async Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> content, string? backupPath, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        try
        {
            var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(content, ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            // Sólo existe si algo falló antes de sustituir el destino.
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
