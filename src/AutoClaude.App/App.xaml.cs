using AutoClaude.Core.Ports;
using AutoClaude.Core.Services;
using AutoClaude.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace AutoClaude.App;

public partial class App : Application
{
    private Window? _window;
    private ServiceProvider? _services;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dbDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoClaude");
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "autoclaude.db");
        var settingsPath = Path.Combine(dbDir, "settings.json");
        var connectionString = $"Data Source={dbPath}";

        var services = new ServiceCollection();
        var notifier = new WinUiOrchestrationNotifier();
        AppComposition.AddAutoClaudeServices(services, connectionString, settingsPath, notifier);

        _services = services.BuildServiceProvider();
        await _services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();

        _window = new MainWindow(
            _services.GetRequiredService<SessionService>(),
            notifier,
            _services.GetRequiredService<IExecutionRecordRepository>());
        _window.Activate();
    }
}
