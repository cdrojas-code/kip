using System.Diagnostics;
using System.Globalization;
using Microsoft.Toolkit.Uwp.Notifications;
using StickyNotes.Core.Abstractions;
using StickyNotes.Core.Models;
using Windows.UI.Notifications;

namespace StickyNotes.Platform;

/// <summary>
/// Recordatorios como notificaciones (toast) de Windows usando <see cref="ToastNotificationManagerCompat"/>
/// (funciona en apps Win32 sin empaquetar desde el build 17763).
/// <para>
/// Al hacer clic en la notificación o en "Abrir nota" se dispara <see cref="NotificationActivated"/>
/// con el Id de la nota, en un hilo que NO es de UI (usar Dispatcher en la App).
/// </para>
/// <para>
/// Plan B: si las notificaciones están deshabilitadas (por el usuario o por directiva de grupo) o la
/// API falla, se dispara <see cref="FallbackRequested"/> de forma síncrona en el hilo que llamó a
/// <see cref="ShowReminder"/>, para que la App muestre su propia ventana emergente.
/// </para>
/// Si la App se inicia porque el usuario pulsó un toast con la App cerrada, Windows la lanza con el
/// argumento <c>-ToastActivated</c>; el evento se recibe igualmente una vez creado este objeto.
/// </summary>
public sealed class ToastNotifier : INotifier, IDisposable
{
    internal const string NoteIdArgument = "noteId";
    internal const string ActionArgument = "action";
    internal const string OpenAction = "open";
    internal const int MaxSnippetLength = 120;

    /// <summary>Texto genérico que se muestra en modo privado (sin título ni contenido de la nota).</summary>
    public const string PrivateReminderText = "Tiene un recordatorio pendiente";

    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");
    private readonly Lock _activationLock = new();
    private EventHandler<Guid>? _notificationActivated;
    private Guid? _pendingActivation;
    private bool _subscribed;
    private bool _disposed;

    /// <summary>Crea el notificador y se suscribe a las activaciones de toast.</summary>
    public ToastNotifier()
    {
        try
        {
            ToastNotificationManagerCompat.OnActivated += OnToastActivated;
            _subscribed = true;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"ToastNotifier: no se pudo suscribir a OnActivated: {ex}");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Si una activación llega antes de que haya suscriptores (la App se lanzó al pulsar un toast y aún
    /// está cargando), se guarda la última y se entrega al primer suscriptor en el hilo que se suscribe.
    /// </remarks>
    public event EventHandler<Guid>? NotificationActivated
    {
        add
        {
            Guid? pending;
            lock (_activationLock)
            {
                _notificationActivated += value;
                pending = _pendingActivation;
                _pendingActivation = null;
            }

            if (pending is { } id && value is not null)
            {
                SafeInvoke(value, id);
            }
        }
        remove
        {
            lock (_activationLock)
            {
                _notificationActivated -= value;
            }
        }
    }

    /// <summary>
    /// Si devuelve true, el toast no muestra título ni contenido de la nota (pantalla compartida o
    /// modo presentación): sólo <see cref="PrivateReminderText"/>. Lo asigna la App; se evalúa en <see cref="ShowReminder"/>.
    /// </summary>
    public Func<bool>? IsPrivate { get; set; }

    /// <summary>
    /// Se dispara cuando el toast no se pudo mostrar (notificaciones desactivadas, directiva, error COM…).
    /// La App debe mostrar su propio aviso para la nota recibida. Se invoca en el hilo de <see cref="ShowReminder"/>.
    /// </summary>
    public event EventHandler<Note>? FallbackRequested;

    /// <inheritdoc />
    public void ShowReminder(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            ToastNotifierCompat notifier = ToastNotificationManagerCompat.CreateToastNotifier();
            if (notifier.Setting != NotificationSetting.Enabled)
            {
                Trace.TraceWarning($"ToastNotifier: notificaciones no habilitadas ({notifier.Setting}); se usa el plan B.");
                RaiseFallback(note);
                return;
            }

            bool isPrivate = EvaluatePrivate();
            string id = note.Id.ToString("D", CultureInfo.InvariantCulture);
            var builder = new ToastContentBuilder()
                .AddArgument(NoteIdArgument, id)
                .SetToastScenario(ToastScenario.Reminder)
                .AddHeader("Kip.Reminders", "Recordatorio", string.Empty);

            if (isPrivate)
            {
                // Privacidad del cliente: los toasts no respetan WDA_* y saldrían en la pantalla compartida.
                builder.AddText(PrivateReminderText);
            }
            else
            {
                builder.AddText(string.IsNullOrWhiteSpace(note.Title) ? "Nota sin título" : note.Title.Trim());

                string snippet = BuildSnippet(note.PlainText);
                if (snippet.Length > 0)
                {
                    builder.AddText(snippet);
                }

                if (note.Reminder is not null)
                {
                    builder.AddAttributionText("Vence: " + note.Reminder.DueAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Spanish));
                }
            }

            builder
                .AddButton(new ToastButton()
                    .SetContent("Abrir nota")
                    .AddArgument(ActionArgument, OpenAction)
                    .AddArgument(NoteIdArgument, id))
                .AddButton(new ToastButtonDismiss("Descartar"));

            var toast = new ToastNotification(builder.GetToastContent().GetXml())
            {
                Tag = id[..Math.Min(id.Length, 16)],
                Group = "reminders",
            };
            toast.Failed += (_, args) =>
            {
                Trace.TraceError($"ToastNotifier: el toast falló: 0x{args.ErrorCode?.HResult:X}");
                RaiseFallback(note);
            };
            notifier.Show(toast);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"ToastNotifier.ShowReminder: {ex}");
            RaiseFallback(note);
        }
    }

    /// <summary>
    /// Elimina el registro de notificaciones (AUMID/COM en HKCU) que crea ToastNotificationManagerCompat.
    /// Lo invoca el desinstalador mediante <c>Kip.exe --uninstall-cleanup</c>. Nunca lanza.
    /// </summary>
    public static void RemoveRegistration()
    {
        try
        {
            ToastNotificationManagerCompat.Uninstall();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"ToastNotifier.RemoveRegistration: {ex}");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_subscribed)
        {
            try
            {
                ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"ToastNotifier.Dispose: {ex}");
            }

            _subscribed = false;
        }

        lock (_activationLock)
        {
            _notificationActivated = null;
            _pendingActivation = null;
        }

        FallbackRequested = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>Primeros <see cref="MaxSnippetLength"/> caracteres del texto, en una línea, con "…" si se recorta.</summary>
    internal static string BuildSnippet(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return string.Empty;
        }

        string singleLine = string.Join(' ', plainText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length <= MaxSnippetLength ? singleLine : singleLine[..MaxSnippetLength].TrimEnd() + "…";
    }

    /// <summary>Extrae el Id de nota de los argumentos de activación. Función pura.</summary>
    internal static bool TryGetNoteId(string? arguments, out Guid noteId)
    {
        noteId = Guid.Empty;
        if (string.IsNullOrEmpty(arguments))
        {
            return false;
        }

        try
        {
            ToastArguments args = ToastArguments.Parse(arguments);
            return args.TryGetValue(NoteIdArgument, out string? value) && Guid.TryParse(value, out noteId);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"ToastNotifier: argumentos de activación inválidos: {ex.Message}");
            return false;
        }
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        try
        {
            if (!TryGetNoteId(e.Argument, out Guid noteId))
            {
                return;
            }

            EventHandler<Guid>? handler;
            lock (_activationLock)
            {
                handler = _notificationActivated;
                if (handler is null)
                {
                    _pendingActivation = noteId; // se entregará al primer suscriptor
                }
            }

            if (handler is not null)
            {
                SafeInvoke(handler, noteId);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"ToastNotifier: error al procesar la activación: {ex}");
        }
    }

    private void SafeInvoke(EventHandler<Guid> handler, Guid noteId)
    {
        try
        {
            handler(this, noteId);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"ToastNotifier: un suscriptor de NotificationActivated lanzó: {ex}");
        }
    }

    private bool EvaluatePrivate()
    {
        try
        {
            return IsPrivate?.Invoke() == true;
        }
        catch (Exception ex)
        {
            // Ante la duda, privado.
            Trace.TraceError($"ToastNotifier: IsPrivate lanzó: {ex}");
            return true;
        }
    }

    private void RaiseFallback(Note note)
    {
        try
        {
            FallbackRequested?.Invoke(this, note);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"ToastNotifier: un suscriptor de FallbackRequested lanzó: {ex}");
        }
    }
}
