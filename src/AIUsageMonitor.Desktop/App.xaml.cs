using AIUsageMonitor.Infrastructure;
using AIUsageMonitor.Infrastructure.Persistence;
using AIUsageMonitor.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;
using System.Windows;

namespace AIUsageMonitor.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private ApplicationDataPaths? _paths;

    static App()
    {
        // WPF's font cache consults the legacy lower-case windir environment variable on
        // startup. Some hardened or hosted Windows sessions expose only SystemRoot; keep this
        // process-local compatibility fallback before any WPF resource is loaded.
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("windir")) &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SystemRoot")))
        {
            Environment.SetEnvironmentVariable("windir", Environment.GetEnvironmentVariable("SystemRoot"));
        }
    }

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeManager.Apply(ThemeVariant.Light);

        var storage = StorageStartup.TryInitialize();
        if (!storage.IsAvailable)
        {
            ConfigureFallbackLogging();
            Log.Error(
                storage.Failure,
                "LocalAppData storage is unavailable; starting in degraded no-persistence mode");
            ShowShell(persistenceAvailable: false);
            return;
        }

        try
        {
            _paths = storage.Paths!;
            ConfigureLogging(_paths);

            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices(services =>
                {
                    services.AddInfrastructure(_paths.RootDirectory);
                    services.AddProviders();
                    services.AddDesktopWorkspaceServices();
                })
                .Build();

            await _host.StartAsync();
            Log.Information("AI_Orchestrator started using LocalAppData at {RootDirectory}", _paths.RootDirectory);
            ShowShell(persistenceAvailable: true);
        }
        catch (Exception exception)
        {
            ConfigureFallbackLogging();
            Log.Error(exception, "Application startup completed in degraded no-persistence mode");
            ShowShell(persistenceAvailable: false);
        }
    }

    private void ShowShell(bool persistenceAvailable)
    {
        MainWindow = persistenceAvailable && _host is not null
            ? _host.Services.GetService<MainWindow>() ?? new MainWindow()
            : new MainWindow();
        ((MainWindow)MainWindow).SetPersistenceAvailability(persistenceAvailable);
        MainWindow.Show();
    }

    private static void ConfigureLogging(ApplicationDataPaths paths)
    {
        try
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.Debug()
                .WriteTo.File(
                    Path.Combine(paths.LogsDirectory, "aiusagemonitor-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14)
                .CreateLogger();
        }
        catch (Exception exception)
        {
            ConfigureFallbackLogging(exception);
        }
    }

    private static void ConfigureFallbackLogging(Exception? exception = null)
    {
        try
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.Debug()
                .CreateLogger();
        }
        catch
        {
            Log.Logger = new LoggerConfiguration().CreateLogger();
        }

        if (exception is not null)
        {
            Log.Warning(exception, "File logging is unavailable; debug logging remains active");
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(2));
            _host.Dispose();
        }

        Log.Information("AI_Orchestrator stopped");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
