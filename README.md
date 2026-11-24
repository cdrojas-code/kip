# Kip

**Sticky notes that stay on top of everything — like a post-it stuck to your monitor.**
**Notas adhesivas que siempre están a la vista, como un post-it pegado en el monitor.**

[English](#english) · [Español](#español)

![Platform](https://img.shields.io/badge/Windows-10%20%7C%2011%20%7C%20LTSC%202019%2B-0078D6)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)

---

## English

Kip is a lightweight Windows desktop app for sticky notes that **always stay visible above every other window**. It was designed for people who used to stick paper notes to their screen — case numbers, deadlines, phone numbers — and want the same thing digitally, without the notes getting lost behind other windows.

It works offline, stores everything **only on your computer**, and hides your notes when you share your screen in a meeting.

### Features

| | |
|---|---|
| 📌 **Always on top** | Notes stay above all windows (and re-assert themselves if another app steals the top spot), without stealing keyboard focus. They don't appear in the taskbar or Alt+Tab. |
| 🗂️ **Multiple notes** | Create as many independent notes as you want and place them anywhere, on any monitor. |
| 🎨 **Post-it style** | 6 colors, 3 fixed sizes (small / medium / large), a bold title per note (client, case number…), adjustable font size. |
| ✍️ **Rich text** | Bold, italic, underline, bullets. |
| 🔗 **Hyperlinks** | URLs become links as you type or paste; `Ctrl+K` to link selected text, edit or remove a link; `Ctrl`+click opens it. Only `http`, `https` and `mailto` are allowed. |
| ☑️ **Checklists** | Tasks with checkboxes; *Enter* adds the next one, *Backspace* on an empty one removes it. |
| ⏰ **Reminders** | Set a date and time; you get a Windows notification and the note's badge turns red when it's due. |
| 🕶️ **Screen-share privacy** | Notes are invisible in screen sharing and screenshots (Teams, Zoom, Meet…) while you still see them. Notifications show no content while sharing. |
| 🔒 **Lock position** | Prevents moving a note by accident. |
| 🔍 **Search** | Find any note by title, text or task — case- and accent-insensitive. |
| 🗑️ **Trash** | Deleted notes go to the trash and can be restored for 30 days. |
| 💾 **Safe storage** | Atomic writes (no corruption on power loss), automatic daily backups, export / import. |
| 🚀 **Starts with Windows** | Notes reappear exactly where you left them. |
| ⌨️ **Global hotkey** | `Ctrl+Alt+N` hides / shows all notes. |

### Requirements

- Windows 10 version 1809 or later (**including LTSC 2019 and LTSC 2021**) or Windows 11 (including LTSC), 64-bit.
- Nothing else: the installer includes the .NET runtime. No Microsoft Store, no admin rights, no internet connection needed.

### Installation

1. Download `Kip-Setup-x.y.z.exe` from the [**Releases**](../../releases/latest) page.
2. Run it. It installs for the current user, without administrator rights.
3. Windows SmartScreen may warn *"Windows protected your PC"* because the installer is not code-signed. Click **More info → Run anyway**.

To uninstall, use *Settings → Apps*. **Your notes are kept** in `%LOCALAPPDATA%\Kip` in case you reinstall.

### How to use

- **New note:** double-click the Kip icon in the system tray (next to the clock), or press **+** on any note.
- **Move:** drag the note by its top bar. Use the **lock** button to pin it in place.
- **Color / size / reminder:** buttons on the note's top bar.
- **Formatting and tasks:** click inside the note — a toolbar appears at the bottom (bold, underline, italic, bullets, *add task*, font size).
- **Delete:** the trash-can button moves the note to the trash (*Tray icon → Trash…* to restore it).
- **Hide everything:** `Ctrl+Alt+N`, or *Tray icon → Presentation mode*.
- **Tray menu:** search, trash, export / import, open backup folder, start with Windows, hide on screen share, about, exit.

### Privacy and screen sharing

- Your notes **never leave your computer**: no cloud, no account, no telemetry. Data lives in `%LOCALAPPDATA%\Kip` (`notes.json`, `settings.json`, `backups\`, `logs\`).
- **Windows 10 2004+ / 11 / LTSC 2021:** notes are excluded from screen capture (`WDA_EXCLUDEFROMCAPTURE`). Others in the meeting don't see them; you do.
- **Windows 10 LTSC 2019 (build 17763):** that Windows API doesn't exist there. Kip uses the best available fallback:
  1. Note contents are **always** blacked out in captures (`WDA_MONITOR`), so the text never leaks.
  2. Kip detects screen sharing in common apps (Teams, Zoom, Google Meet in browsers, Webex, TeamViewer, AnyDesk, Skype) and hides the notes automatically.
  3. `Ctrl+Alt+N` and *Presentation mode* are always available as a manual switch.

  Detection is heuristic; if an app isn't detected, its window signature can be added in [`ShareSignatures.cs`](src/StickyNotes.Platform/ShareSignatures.cs).

### Building from source

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download) and, for the installer, [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
winget install Microsoft.DotNet.SDK.10
winget install JRSoftware.InnoSetup

git clone <this-repository-url>
cd <repository-folder>

# Build, run tests, publish (self-contained win-x64) and create the installer:
.\build.ps1 -Version 1.0.0
# → artifacts\installer\Kip-Setup-1.0.0.exe

# Or just run it while developing:
dotnet run --project src\StickyNotes.App
```

### Architecture

A layered C# / WPF solution (MVVM); dependencies point inwards to `Core`. See [ARCHITECTURE.md](ARCHITECTURE.md) for details.

| Project | Responsibility |
|---|---|
| `StickyNotes.Core` | Models, contracts and business logic (notes, trash, search, reminders). No UI or Win32 dependencies; fully unit-tested. |
| `StickyNotes.Infrastructure` | Atomic JSON persistence, backups, export / import. |
| `StickyNotes.Platform` | Windows integration: capture privacy, screen-share detection, global hotkey, autostart, notifications, multi-monitor. |
| `StickyNotes.App` | WPF user interface: note windows, tray icon, search, trash. |
| `tests/` | xUnit tests for Core and Infrastructure. |

> The internal project names keep the original working name *StickyNotes*; the product is called **Kip**.

### Contributing

Issues and pull requests are welcome. Please run `dotnet test StickyNotes.slnx` before submitting, and keep `Core` free of UI / Win32 dependencies.

### License and credits

[MIT](LICENSE) © 2026 Carlos Daniel Rojas.

Created by **Carlos Daniel Rojas ("cdrojas")** with the help of **Claude**, Anthropic's AI assistant.

---

## Español

Kip es una aplicación ligera de notas adhesivas para Windows que **siempre se mantienen visibles por encima de todas las ventanas**. Nació para personas que pegaban papelitos en la pantalla —números de expediente, plazos, teléfonos— y quieren lo mismo en digital, sin que las notas se pierdan detrás de otras ventanas.

Funciona sin internet, guarda todo **sólo en tu computadora** y oculta las notas cuando compartes pantalla en una reunión.

### Funciones

| | |
|---|---|
| 📌 **Siempre encima** | Las notas quedan sobre todas las ventanas (y vuelven arriba si otra app les quita el lugar), sin robar el foco del teclado. No aparecen en la barra de tareas ni en Alt+Tab. |
| 🗂️ **Varias notas** | Crea todas las notas que quieras y colócalas donde quieras, en cualquier monitor. |
| 🎨 **Estilo post-it** | 6 colores, 3 tamaños fijos (pequeño / mediano / grande), título en negrita por nota (cliente, expediente…), tamaño de letra ajustable. |
| ✍️ **Texto con formato** | Negrita, cursiva, subrayado y viñetas. |
| 🔗 **Hipervínculos** | Las direcciones se convierten en vínculos al escribirlas o pegarlas; `Ctrl+K` enlaza el texto seleccionado, edita o quita un vínculo; `Ctrl`+clic lo abre. Sólo se permiten `http`, `https` y `mailto`. |
| ☑️ **Listas de tareas** | Tareas con casilla; *Enter* agrega la siguiente y *Retroceso* en una vacía la elimina. |
| ⏰ **Recordatorios** | Elige fecha y hora; recibirás una notificación de Windows y la etiqueta de la nota se pone roja al vencer. |
| 🕶️ **Privacidad al compartir pantalla** | Las notas son invisibles en pantallas compartidas y capturas (Teams, Zoom, Meet…), pero tú sí las ves. Las notificaciones no muestran contenido mientras compartes. |
| 🔒 **Bloquear posición** | Evita mover una nota por accidente. |
| 🔍 **Búsqueda** | Encuentra cualquier nota por título, texto o tarea, sin importar mayúsculas ni tildes. |
| 🗑️ **Papelera** | Las notas eliminadas van a la papelera y se pueden restaurar durante 30 días. |
| 💾 **Guardado seguro** | Escritura atómica (no se corrompe si se corta la luz), copias de seguridad diarias automáticas, exportar / importar. |
| 🚀 **Inicia con Windows** | Las notas reaparecen exactamente donde las dejaste. |
| ⌨️ **Atajo global** | `Ctrl+Alt+N` oculta / muestra todas las notas. |

### Requisitos

- Windows 10 versión 1809 o posterior (**incluye LTSC 2019 y LTSC 2021**) o Windows 11 (incluye LTSC), 64 bits.
- Nada más: el instalador incluye .NET. No necesita Microsoft Store, permisos de administrador ni conexión a internet.

### Instalación

1. Descarga `Kip-Setup-x.y.z.exe` desde la página de [**Releases**](../../releases/latest).
2. Ejecútalo. Se instala para el usuario actual, sin permisos de administrador.
3. Windows SmartScreen puede mostrar *"Windows protegió su PC"* porque el instalador no está firmado digitalmente. Pulsa **Más información → Ejecutar de todas formas**.

Para desinstalar, usa *Configuración → Aplicaciones*. **Tus notas se conservan** en `%LOCALAPPDATA%\Kip` por si vuelves a instalar.

### Cómo se usa

- **Nota nueva:** doble clic en el icono de Kip en la bandeja del sistema (junto al reloj), o el botón **+** de cualquier nota.
- **Mover:** arrastra la nota desde su franja superior. El botón del **candado** la fija en su lugar.
- **Color / tamaño / recordatorio:** botones de la franja superior de la nota.
- **Formato y tareas:** haz clic dentro de la nota; abajo aparece una barra (negrita, subrayado, cursiva, viñetas, *vínculo*, *agregar tarea*, tamaño de letra).
- **Eliminar:** el botón de la papelera envía la nota a la papelera (*Icono de la bandeja → Papelera…* para restaurarla).
- **Ocultar todo:** `Ctrl+Alt+N`, o *Icono de la bandeja → Modo presentación*.
- **Menú de la bandeja:** buscar, papelera, exportar / importar, abrir carpeta de copias, iniciar con Windows, ocultar al compartir pantalla, acerca de, salir.

### Privacidad y pantalla compartida

- Tus notas **nunca salen de tu computadora**: sin nube, sin cuenta, sin telemetría. Los datos están en `%LOCALAPPDATA%\Kip` (`notes.json`, `settings.json`, `backups\`, `logs\`).
- **Windows 10 2004+ / 11 / LTSC 2021:** las notas se excluyen de la captura de pantalla (`WDA_EXCLUDEFROMCAPTURE`). Los demás participantes no las ven; tú sí.
- **Windows 10 LTSC 2019 (build 17763):** esa función de Windows no existe ahí. Kip usa la mejor alternativa disponible:
  1. El contenido de las notas aparece **siempre** tapado en negro en las capturas (`WDA_MONITOR`), así el texto nunca se filtra.
  2. Kip detecta cuándo compartes pantalla en las apps más comunes (Teams, Zoom, Google Meet en el navegador, Webex, TeamViewer, AnyDesk, Skype) y oculta las notas automáticamente.
  3. `Ctrl+Alt+N` y el *Modo presentación* siempre están disponibles como interruptor manual.

  La detección es heurística; si alguna app no se detecta, su firma de ventana se puede agregar en [`ShareSignatures.cs`](src/StickyNotes.Platform/ShareSignatures.cs).

### Compilar desde el código fuente

Requisitos: [.NET 10 SDK](https://dotnet.microsoft.com/download) y, para el instalador, [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
winget install Microsoft.DotNet.SDK.10
winget install JRSoftware.InnoSetup

git clone <url-de-este-repositorio>
cd <carpeta-del-repositorio>

# Compila, ejecuta las pruebas, publica (self-contained win-x64) y genera el instalador:
.\build.ps1 -Version 1.0.0
# → artifacts\installer\Kip-Setup-1.0.0.exe

# O ejecútalo directamente mientras desarrollas:
dotnet run --project src\StickyNotes.App
```

### Arquitectura

Solución en C# / WPF por capas (MVVM); las dependencias apuntan hacia `Core`. Detalles en [ARCHITECTURE.md](ARCHITECTURE.md).

| Proyecto | Responsabilidad |
|---|---|
| `StickyNotes.Core` | Modelos, contratos y lógica de negocio (notas, papelera, búsqueda, recordatorios). Sin dependencias de UI ni Win32; con pruebas unitarias. |
| `StickyNotes.Infrastructure` | Persistencia JSON atómica, copias de seguridad, exportar / importar. |
| `StickyNotes.Platform` | Integración con Windows: privacidad de captura, detección de pantalla compartida, atajo global, inicio con Windows, notificaciones, varios monitores. |
| `StickyNotes.App` | Interfaz WPF: ventanas de nota, icono de bandeja, búsqueda, papelera. |
| `tests/` | Pruebas xUnit de Core e Infrastructure. |

> Los proyectos internos conservan el nombre de trabajo original *StickyNotes*; el producto se llama **Kip**.

### Cómo contribuir

Se aceptan *issues* y *pull requests*. Ejecuta `dotnet test StickyNotes.slnx` antes de enviar cambios y mantén `Core` sin dependencias de UI / Win32.

### Licencia y créditos

[MIT](LICENSE) © 2026 Carlos Daniel Rojas.

Creado por **Carlos Daniel Rojas («cdrojas»)** con la ayuda de **Claude**, el asistente de IA de Anthropic.
