namespace StickyNotes.Core.Models;

public sealed class AppSettings
{
    public const string DefaultToggleHotkey = "Ctrl+Alt+N";
    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 3650;
    public const double MinFontSize = 8;
    public const double MaxFontSize = 48;
    public const double DefaultFontSizeValue = 14;

    public bool StartWithWindows { get; set; } = true;

    /// <summary>Excluir las notas de capturas / pantalla compartida.</summary>
    public bool HideOnScreenShare { get; set; } = true;

    /// <summary>Atajo global para ocultar/mostrar todas las notas. Formato: "Ctrl+Alt+N".</summary>
    public string ToggleHotkey { get; set; } = DefaultToggleHotkey;

    public bool ConfirmDelete { get; set; } = true;

    public NoteColor DefaultColor { get; set; } = NoteColor.Yellow;
    public NoteSize DefaultSize { get; set; } = NoteSize.Medium;
    public double DefaultFontSize { get; set; } = DefaultFontSizeValue;

    public int BackupRetentionDays { get; set; } = 30;
    public int TrashRetentionDays { get; set; } = 30;

    /// <summary>
    /// Corrige valores fuera de rango (settings.json editado a mano o dañado) para que nunca
    /// provoquen excepciones al arrancar. Devuelve la misma instancia.
    /// </summary>
    public AppSettings Normalize()
    {
        BackupRetentionDays = Math.Clamp(BackupRetentionDays, MinRetentionDays, MaxRetentionDays);
        TrashRetentionDays = Math.Clamp(TrashRetentionDays, MinRetentionDays, MaxRetentionDays);
        DefaultFontSize = ClampFontSize(DefaultFontSize);

        if (!Enum.IsDefined(DefaultColor))
        {
            DefaultColor = NoteColor.Yellow;
        }

        if (!Enum.IsDefined(DefaultSize))
        {
            DefaultSize = NoteSize.Medium;
        }

        if (string.IsNullOrWhiteSpace(ToggleHotkey))
        {
            ToggleHotkey = DefaultToggleHotkey;
        }

        return this;
    }

    /// <summary>Limita un tamaño de letra al rango admitido (NaN/infinito = valor por defecto).</summary>
    public static double ClampFontSize(double size) =>
        double.IsFinite(size) ? Math.Clamp(size, MinFontSize, MaxFontSize) : DefaultFontSizeValue;
}
