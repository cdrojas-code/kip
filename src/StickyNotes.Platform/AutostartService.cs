using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using StickyNotes.Core.Abstractions;

namespace StickyNotes.Platform;

/// <summary>
/// Arranque con Windows mediante el valor <c>Kip</c> en
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> (no requiere administrador).
/// Valor: <c>"&lt;ruta del exe&gt;" --autostart</c>. Los errores de registro se registran con
/// <see cref="Trace"/> y no se propagan.
/// </summary>
public sealed class AutostartService : IAutostartService
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "Kip";

    /// <summary>Nombre anterior de la app (StickyNotes): se elimina al activar/desactivar para no arrancar el exe viejo.</summary>
    internal const string LegacyValueName = "StickyNotes";
    internal const string AutostartArgument = "--autostart";

    /// <inheritdoc />
    /// <remarks>True sólo si el valor existe y apunta al ejecutable actual (evita falsos positivos tras mover la instalación).</remarks>
    public bool IsEnabled
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                if (key?.GetValue(ValueName) is not string command)
                {
                    return false;
                }

                string? exe = Environment.ProcessPath;
                string? registered = ExtractExecutablePath(command);
                return exe is not null && registered is not null && PathsEqual(exe, registered);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"AutostartService.IsEnabled: {ex}");
                return false;
            }
        }
    }

    /// <inheritdoc />
    public void Enable()
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            Trace.TraceError("AutostartService.Enable: no se pudo determinar la ruta del ejecutable.");
            return;
        }

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.SetValue(ValueName, BuildCommand(exe), RegistryValueKind.String);
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"AutostartService.Enable: {ex}");
        }
    }

    /// <inheritdoc />
    public void Disable()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            key?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"AutostartService.Disable: {ex}");
        }
    }

    /// <summary>Línea de comandos que se escribe en el registro.</summary>
    internal static string BuildCommand(string exePath) => $"\"{exePath}\" {AutostartArgument}";

    /// <summary>Extrae la ruta del ejecutable de una línea de comandos (entre comillas o hasta el primer espacio).</summary>
    internal static string? ExtractExecutablePath(string command)
    {
        string trimmed = command.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed[0] == '"')
        {
            int end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : null;
        }

        int space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? trimmed : trimmed[..space];
    }

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
