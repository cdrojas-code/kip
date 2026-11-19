using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using StickyNotes.App.Composition;
using StickyNotes.App.Logging;
using StickyNotes.App.Services;

namespace StickyNotes.App;

/// <summary>Punto de entrada: instancia única, manejo global de errores y arranque/cierre ordenado.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Los recursos se liberan en ExitAsync / OnExit, el ciclo de vida de WPF.")]
public partial class App : Application
{
    private const string AutostartArgument = "--autostart";
    private const string UninstallCleanupArgument = "--uninstall-cleanup";
    private const string FriendlyError =
        "Se produjo un error inesperado. Sus notas se han guardado.\n\n" +
        "Si el problema se repite, el detalle está en la carpeta de registros:\n%LOCALAPPDATA%\\Kip\\logs";

    private static readonly TimeSpan EmergencyFlushTimeout = TimeSpan.FromSeconds(5);

    private readonly FileLogger _log = new();
    private SingleInstanceGuard? _instance;
    private ServiceProvider? _services;
    private AppController? _controller;
    private bool _isShuttingDown;
    private bool _isShowingError;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a => string.Equals(a, UninstallCleanupArgument, StringComparison.OrdinalIgnoreCase)))
        {
            // Invocado por el desinstalador: limpia autoarranque y registro de notificaciones. No toca las notas.
            new StickyNotes.Platform.AutostartService().Disable();
            StickyNotes.Platform.ToastNotifier.RemoveRegistration();
            Shutdown();
            return;
        }

        RegisterGlobalExceptionHandlers();

        _instance = new SingleInstanceGuard();
        if (!_instance.IsFirstInstance)
        {
            // Ya hay una instancia: que cree una nota nueva y esta termina.
            _instance.SignalFirstInstance();
            _instance.Dispose();
            Shutdown();
            return;
        }

        try
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            _services = ServiceRegistration.Build(_log);
            _controller = _services.GetRequiredService<AppController>();
            _controller.ExitRequested += async (_, _) => await ExitAsync();

            var startedWithWindows = e.Args.Any(a => string.Equals(a, AutostartArgument, StringComparison.OrdinalIgnoreCase));
            await _controller.StartAsync(startedWithWindows);
            _instance.ListenForSignals(_controller.RequestNewNote);
        }
        catch (Exception ex)
        {
            _log.Error("Error fatal al iniciar", ex);
            MessageBox.Show(
                "Kip no pudo iniciarse. Sus notas no se han modificado.\n\n" + ex.Message,
                "Kip", MessageBoxButton.OK, MessageBoxImage.Error);
            await ExitAsync();
        }
    }

    /// <summary>Salida ordenada: guarda, libera bandeja/atajos/detector y cierra.</summary>
    private async Task ExitAsync()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;
        try
        {
            if (_controller is not null) await _controller.StopAsync();
            _services?.Dispose();
        }
        catch (Exception ex)
        {
            _log.Error("Error al cerrar", ex);
        }
        finally
        {
            _instance?.Dispose();
            Shutdown();
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Apagado / cierre de sesión: guardado síncrono (no hay tiempo para la salida asíncrona normal).
        _log.Info($"Fin de sesión de Windows ({e.ReasonSessionEnding})");
        _controller?.FlushNow(EmergencyFlushTimeout);
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Red de seguridad si se sale sin pasar por ExitAsync (p. ej. tras SessionEnding).
        if (!_isShuttingDown)
        {
            _controller?.FlushNow(EmergencyFlushTimeout);
            try { _services?.Dispose(); } catch (Exception ex) { _log.Warn("Error al liberar servicios", ex); }
            _instance?.Dispose();
        }
        base.OnExit(e);
    }

    // ───────────── Errores globales ─────────────

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log.Error("Excepción no controlada (UI)", e.Exception);
        _controller?.FlushNow(EmergencyFlushTimeout);
        e.Handled = true;
        ShowFriendlyError();
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _log.Error($"Excepción no controlada (dominio, terminando={e.IsTerminating})", e.ExceptionObject as Exception);
        _controller?.FlushNow(EmergencyFlushTimeout);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log.Error("Excepción no observada en tarea", e.Exception);
        e.SetObserved();
    }

    private void ShowFriendlyError()
    {
        // Evita una cascada de mensajes si el error se repite mientras se muestra el primero.
        if (_isShowingError) return;
        _isShowingError = true;
        try
        {
            MessageBox.Show(FriendlyError, "Kip", MessageBoxButton.OK, MessageBoxImage.Warning,
                MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
        }
        finally
        {
            _isShowingError = false;
        }
    }
}
