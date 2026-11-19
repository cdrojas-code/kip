using Microsoft.Extensions.DependencyInjection;
using StickyNotes.App.Logging;
using StickyNotes.App.Services;
using StickyNotes.Core.Abstractions;
using StickyNotes.Core.Services;
using StickyNotes.Infrastructure;
using StickyNotes.Platform;

namespace StickyNotes.App.Composition;

/// <summary>Raíz de composición: único lugar que conoce las clases concretas de todas las capas.</summary>
public static class ServiceRegistration
{
    /// <summary>Construir en el hilo de UI: algunos servicios (hotkeys, timers, ventanas) quedan ligados a él.</summary>
    public static ServiceProvider Build(FileLogger logger)
    {
        var services = new ServiceCollection();

        // Core
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(sp => new NoteService(sp.GetRequiredService<INoteRepository>(), sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<IBackupService>(), sp.GetRequiredService<IClock>()));
        services.AddSingleton<INoteService>(sp => sp.GetRequiredService<NoteService>());
        services.AddSingleton<IReminderMonitor>(sp =>
        {
            var monitor = new ReminderMonitor(sp.GetRequiredService<INoteService>(), sp.GetRequiredService<INotifier>(), sp.GetRequiredService<IClock>());
            monitor.NotifyFailed += (_, ex) => logger.Error("Error al mostrar un recordatorio", ex);
            return monitor;
        });

        // Infrastructure
        services.AddSingleton<IAppPaths>(_ => new AppPaths());
        services.AddSingleton<INoteRepository, JsonNoteRepository>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IBackupService, BackupService>();

        // Platform
        services.AddSingleton<IScreenPrivacyService>(_ => new ScreenPrivacyService());
        services.AddSingleton<IScreenShareDetector>(_ => new ScreenShareDetector());
        services.AddSingleton<IGlobalHotkeyService>(_ => new GlobalHotkeyService());
        services.AddSingleton<IAutostartService>(_ => new AutostartService());
        services.AddSingleton<IScreenBoundsService>(_ => new ScreenBoundsService());
        services.AddSingleton(_ => new ToastNotifier());
        services.AddSingleton<INotifier>(sp => sp.GetRequiredService<ToastNotifier>());

        // App
        services.AddSingleton(logger);
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<NoteWindowManager>();
        services.AddSingleton<AppWindows>();
        services.AddSingleton<ImportExportService>();
        services.AddSingleton<ScreenSharePrivacyCoordinator>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<ReminderScheduler>();
        services.AddSingleton<AppController>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }
}
