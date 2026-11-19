namespace StickyNotes.App.Services;

/// <summary>Motivos por los que las notas están ocultas. Se llevan por separado para que el
/// autoocultado (pantalla compartida) no vuelva a mostrar notas que el usuario ocultó a mano.</summary>
[Flags]
public enum HideReasons
{
    None = 0,

    /// <summary>Atajo global / "Ocultar/mostrar todas".</summary>
    Manual = 1,

    /// <summary>"Modo presentación" en la bandeja.</summary>
    Presentation = 2,

    /// <summary>Automático: se detectó pantalla compartida (modo BlackoutWithAutoHide).</summary>
    ScreenShare = 4,
}
