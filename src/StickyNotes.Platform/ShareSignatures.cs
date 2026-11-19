namespace StickyNotes.Platform;

/// <summary>
/// Datos de una ventana de nivel superior visible, usados por la heurística de <see cref="ScreenShareDetector"/>.
/// </summary>
/// <param name="ProcessName">Nombre del ejecutable, con o sin ".exe" (p. ej. "Zoom" o "Zoom.exe").</param>
/// <param name="ClassName">Clase Win32 de la ventana.</param>
/// <param name="Title">Texto (título) de la ventana.</param>
internal sealed record WindowInfo(string ProcessName, string ClassName, string Title);

/// <summary>
/// Firma de una ventana que indica que hay una sesión de pantalla compartida activa.
/// Una ventana coincide si su proceso es <paramref name="ProcessName"/> (sin distinguir mayúsculas,
/// ".exe" opcional) y además coinciden TODOS los patrones no nulos.
/// Al menos uno de los patrones debe ser no nulo (una firma sin patrones se ignora, para no
/// disparar con cualquier ventana del proceso).
/// </summary>
/// <param name="ProcessName">Ejecutable, p. ej. "Zoom.exe".</param>
/// <param name="ClassNamePattern">Regex (insensible a mayúsculas) contra la clase de la ventana, o null.</param>
/// <param name="TitlePattern">Regex (insensible a mayúsculas) contra el título de la ventana, o null.</param>
internal sealed record ShareSignature(string ProcessName, string? ClassNamePattern, string? TitlePattern);

/// <summary>
/// Catálogo de firmas de "pantalla compartida". ÚNICO lugar a mantener cuando una app de reuniones
/// cambie sus ventanas o se quiera soportar otra.
/// <para>
/// Cómo añadir una firma: abrir la herramienta de reuniones, empezar a compartir y observar las ventanas
/// de nivel superior (Spy++, "Window Detective" o PowerShell + EnumWindows). Elegir una ventana que SÓLO
/// exista mientras se comparte (barra de control, borde de compartición, aviso del navegador) y añadir su
/// proceso + clase y/o título. Los patrones son regex .NET, se evalúan con IgnoreCase y tiempo límite.
/// Preferir patrones anclados (^...) para evitar falsos positivos con títulos de chats o documentos.
/// </para>
/// <para>
/// Política ante la duda: un falso positivo sólo oculta las notas temporalmente (molesto pero seguro);
/// un falso negativo puede exponer información confidencial de clientes. Por eso se aceptan firmas
/// "best-effort" algo amplias, siempre limitadas al proceso concreto.
/// </para>
/// Nivel de confianza indicado en cada bloque: [ALTA] verificado/ampliamente documentado,
/// [MEDIA] conocido pero puede variar entre versiones, [BAJA] suposición razonable (best-effort).
/// </summary>
internal static class ShareSignatures
{
    // Fragmentos de título reutilizados (inglés + español).
    private const string GenericSharingTitle =
        @"(you('| a)re (now )?(screen )?sharing|screen sharing|sharing (your|the) screen|" +
        @"est[aá]s? compartiendo|compartiendo (la |tu |su )?pantalla|uso compartido de pantalla)";

    /// <summary>Firmas por defecto.</summary>
    internal static readonly IReadOnlyList<ShareSignature> Default =
    [
        // ── Zoom (Zoom.exe) ─────────────────────────────────────────────────────────────────
        // [MEDIA] "ZPToolBarParentWnd" es la barra flotante de controles que Zoom muestra mientras
        // el usuario comparte pantalla (en reunión normal la barra está integrada en la ventana).
        new("Zoom.exe", @"^ZPToolBarParentWnd$", null),
        // [MEDIA] Ventanas de contenido/compartición con título que menciona compartir.
        // "ZPContentViewWndClass" es la ventana de la reunión; sólo cuenta si el título habla de compartir.
        new("Zoom.exe", @"^ZPContentViewWndClass$", @"(shar(e|ing)|compartiendo|compartir)"),
        // [BAJA] Avisos tipo "You are screen sharing" / "Está compartiendo pantalla" en cualquier ventana de Zoom.
        new("Zoom.exe", null, GenericSharingTitle),

        // ── Microsoft Teams clásico (Teams.exe, Electron) ───────────────────────────────────
        // [ALTA] Al compartir aparece una ventana pequeña "Sharing control bar | Microsoft Teams"
        // (es-ES: "Barra de control de uso compartido | Microsoft Teams"), clase Chrome_WidgetWin_1.
        new("Teams.exe", null, @"^\s*(sharing control bar|barra de control de uso compartido|barra de control del uso compartido)"),
        // [BAJA] Barra de moderador / presentador en versiones recientes.
        new("Teams.exe", null, @"^\s*(presenter toolbar|presenting|barra de herramientas del (moderador|presentador)|presentando)\b"),

        // ── Microsoft Teams nuevo (ms-teams.exe, WebView2) ──────────────────────────────────
        // [MEDIA] El nuevo Teams conserva la ventana flotante de control de compartición; los títulos
        // se han visto en inglés y español con el mismo texto que el clásico.
        new("ms-teams.exe", null, @"^\s*(sharing control bar|barra de control de uso compartido|barra de control del uso compartido)"),
        // [BAJA] Barra del presentador / "Presentando".
        new("ms-teams.exe", null, @"^\s*(presenter toolbar|presenting|barra de herramientas del (moderador|presentador)|presentando)\b"),
        new("ms-teams.exe", null, GenericSharingTitle),

        // ── Cisco Webex ────────────────────────────────────────────────────────────────────
        // [BAJA] Webex App (CiscoCollabHost.exe / Webex.exe) y Webex Meetings clásico (atmgr.exe,
        // webexmta.exe). Al compartir muestran una barra/panel flotante cuyo título menciona compartir.
        new("CiscoCollabHost.exe", null, GenericSharingTitle + @"|^\s*(sharing|compartiendo)\b"),
        new("Webex.exe", null, GenericSharingTitle + @"|^\s*(sharing|compartiendo)\b"),
        new("atmgr.exe", null, GenericSharingTitle + @"|^\s*(sharing|compartiendo)\b"),
        new("webexmta.exe", null, GenericSharingTitle + @"|^\s*(sharing|compartiendo)\b"),

        // ── Navegadores: Google Meet y cualquier reunión web (Teams web, Zoom web, Jitsi…) ──
        // [ALTA] Chrome/Edge (Chromium) muestran una ventana flotante propia (Chrome_WidgetWin_1)
        // "meet.google.com is sharing your screen." / "... is sharing a window." y en español
        // "meet.google.com está compartiendo tu pantalla." / "... una ventana". Es genérico por origen,
        // así que cubre cualquier web que comparta pantalla.
        new("chrome.exe", null, BrowserSharingTitle),
        new("msedge.exe", null, BrowserSharingTitle),
        new("brave.exe", null, BrowserSharingTitle),
        new("opera.exe", null, BrowserSharingTitle),
        new("vivaldi.exe", null, BrowserSharingTitle),
        // [MEDIA] Firefox usa un "indicador de compartición" (título "Firefox - Sharing Indicator" /
        // "Firefox - Indicador de compartición"). OJO: también aparece si sólo se comparte cámara/micrófono;
        // se acepta el falso positivo (ver política arriba).
        new("firefox.exe", null, @"(sharing indicator|indicador de (compartici[oó]n|uso compartido))|" + BrowserSharingTitle),

        // ── Escritorio remoto / soporte ────────────────────────────────────────────────────
        // [MEDIA] TeamViewer: durante una sesión entrante muestra el "TeamViewer Panel" (clase TV_ControlWin).
        new("TeamViewer.exe", @"^TV_ControlWin$", null),
        new("TeamViewer.exe", null, @"^\s*(teamviewer panel|panel de teamviewer)"),
        // [BAJA] AnyDesk: la ventana de sesión entrante / "accept" muestra el alias remoto; su título suele
        // contener "session"/"sesión" o "connected". Muy dependiente de la versión.
        new("AnyDesk.exe", null, @"(\bsession\b|\bsesi[oó]n\b|\bconnected\b|\bconectado\b)"),

        // ── Skype ──────────────────────────────────────────────────────────────────────────
        // [BAJA] Skype 8+ (Skype.exe, Electron) y Skype Empresarial (lync.exe): barra "You are sharing" /
        // "Estás compartiendo" / "Presenting" durante la compartición.
        new("Skype.exe", null, GenericSharingTitle + @"|^\s*(presenting|presentando)\b"),
        new("lync.exe", null, GenericSharingTitle + @"|(currently presenting|presentando actualmente)"),
    ];

    /// <summary>Aviso de compartición de navegadores Chromium (y Firefox reciente), inglés y español.</summary>
    private const string BrowserSharingTitle =
        @"(\bis sharing (your screen|your entire screen|a window|this tab|a tab)|" +
        @"est[aá] compartiendo (tu|su|la) pantalla|est[aá] compartiendo (una|esta) (ventana|pesta[nñ]a)|" +
        @"comparte (tu|su) pantalla)";
}
