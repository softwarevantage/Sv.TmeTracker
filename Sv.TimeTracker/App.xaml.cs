using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Sv.TimeTracker.Data;
using Sv.TimeTracker.Data.Repositories;
using Sv.TimeTracker.Services;
using Sv.TimeTracker.ViewModels;

namespace Sv.TimeTracker;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public static bool IsSmokeTestMode { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IsSmokeTestMode = e.Args.Any(a => string.Equals(a, "--smoke-test", StringComparison.OrdinalIgnoreCase));
        var customDbPath = Environment.GetEnvironmentVariable("SV_TIMETRACKER_DB_PATH");

        var services = new ServiceCollection();
        ConfigureServices(services, customDbPath);
        _serviceProvider = services.BuildServiceProvider();

        var connectionFactory = _serviceProvider.GetRequiredService<ISqliteConnectionFactory>();
        DatabaseHelper.ConfigureDefaultFactory(connectionFactory);
        connectionFactory.Initialize(seedStarterWorkItems: true);

        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        await mainViewModel.InitializeAsync();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        if (IsSmokeTestMode)
        {
            await Task.Delay(350);
            mainWindow.RequestExplicitExit();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services, string? customDbPath)
    {
        services.AddSingleton<ISqliteConnectionFactory>(_ => new SqliteConnectionFactory(customDbPath));
        services.AddSingleton<IWorkItemRepository, WorkItemRepository>();
        services.AddSingleton<ITimeEntryRepository, TimeEntryRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<ITimerService, TimerService>();
        services.AddSingleton<IAudioChimePlayer, SynthesizedAudioChimePlayer>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();

        services.AddSingleton<TimerViewModel>();
        services.AddSingleton<WorkItemsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
