# Kip — Arquitectura

App de notas adhesivas "siempre encima" para Windows 10/11 (incluye LTSC 2019, build 17763).
Los proyectos internos conservan el nombre de trabajo `StickyNotes.*`; el producto (exe, instalador, datos) se llama **Kip**.
Stack: C# / .NET 10 / WPF, publicada *self-contained* (no requiere .NET instalado). Instalador: Inno Setup.

## Capas (dependencias apuntan hacia Core)

```
StickyNotes.App  (WPF, MVVM, composición DI)
   ├──> StickyNotes.Core            (modelos, contratos, lógica de negocio — sin dependencias de UI ni Win32)
   ├──> StickyNotes.Infrastructure  (persistencia JSON, backups)        ──> Core
   └──> StickyNotes.Platform        (Win32: privacidad de captura, hotkeys, autoarranque, toasts, monitores) ──> Core
```

| Proyecto | TFM | Responsabilidad |
|---|---|---|
| `src/StickyNotes.Core` | net10.0 | Modelos (`Note`, `AppSettings`…), interfaces, `NoteService`, `ReminderMonitor`, `NoteSearch`. 100 % testeable. |
| `src/StickyNotes.Infrastructure` | net10.0 | `JsonNoteRepository`, `JsonSettingsStore` (escritura atómica), `BackupService` (diario + export/import), `AppPaths`. |
| `src/StickyNotes.Platform` | net10.0-windows10.0.17763.0 | `ScreenPrivacyService`, `ScreenShareDetector`, `GlobalHotkeyService`, `AutostartService`, `ToastNotifier`, `ScreenBoundsService`. |
| `src/StickyNotes.App` | net10.0-windows10.0.17763.0 | Ventanas de nota, bandeja del sistema, búsqueda, papelera, ajustes. MVVM con CommunityToolkit.Mvvm. |
| `tests/*` | | xUnit para Core e Infrastructure. |
| `installer/StickyNotes.iss` | | Instalador clásico (menú Inicio, autoarranque opcional). |

## Reglas
- **Core no conoce WPF ni Win32.** Todo lo de plataforma entra por interfaces de `StickyNotes.Core.Abstractions`.
- Los contratos de Core (`Contracts.cs`, `Models/`) son la fuente de verdad; cambiarlos requiere actualizar este documento.
- Datos sólo locales: `%LOCALAPPDATA%\Kip\` (`notes.json`, `settings.json`, `backups\`, `logs\`). Se migran automáticamente desde `%LOCALAPPDATA%\StickyNotes\` (nombre anterior).
- `Note.Left/Top` se guardan en píxeles físicos (correcto con monitores de distinta escala).
- Escritura atómica (archivo temporal + `File.Replace`/`File.Move`) para no corromper notas si se apaga el PC.
- Guardado con *debounce* (≈700 ms) tras cada cambio; guardado inmediato al cerrar.

## Privacidad al compartir pantalla
- Build ≥ 19041 (Win10 2004+, LTSC 2021, Win11): `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` → invisibles en capturas/compartición, visibles para el usuario.
- Build < 19041 (LTSC 2019): `WDA_MONITOR` → el contenido **nunca** se filtra (aparece recuadro negro) **+** `ScreenShareDetector` (heurística de ventanas de Teams/Zoom/Webex/Meet) oculta las notas automáticamente mientras se comparte **+** hotkey global Ctrl+Alt+N y "Modo presentación" en la bandeja.

## Datos
- `Note.RichContent`: FlowDocument serializado en XAML (formato básico: negrita, subrayado, viñetas, tamaño).
- `Note.PlainText`: texto plano derivado para búsqueda.
- Papelera: `Note.DeletedAt != null`. Se purga automáticamente tras `TrashRetentionDays` (30).
- Backups: uno por día en `backups\notes-yyyyMMdd.json`, retención `BackupRetentionDays` (30).
