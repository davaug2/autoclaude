using AutoClaude.Core.PhaseHandlers;
using AutoClaude.Core.Ports;
using AutoClaude.Core.Services;
using AutoClaude.Infrastructure.Configuration;
using AutoClaude.Infrastructure.Data;
using AutoClaude.Infrastructure.Executors;
using AutoClaude.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoClaude.App;

public static class AppComposition
{
    public static void AddAutoClaudeServices(
        IServiceCollection services,
        string connectionString,
        string settingsPath,
        WinUiOrchestrationNotifier uiNotifier)
    {
        services.AddSingleton(uiNotifier);
        services.AddSingleton<IOrchestrationNotifier>(sp => sp.GetRequiredService<WinUiOrchestrationNotifier>());

        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        services.AddSingleton(new ConnectionFactory(connectionString));
        services.AddSingleton<IDatabaseInitializer, SqliteDatabaseInitializer>();

        services.AddSingleton<IWorkModelRepository, WorkModelRepository>();
        services.AddSingleton<IPhaseRepository, PhaseRepository>();
        services.AddSingleton<ISessionRepository, SessionRepository>();
        services.AddSingleton<ITaskRepository, TaskRepository>();
        services.AddSingleton<ISubtaskRepository, SubtaskRepository>();
        services.AddSingleton<IExecutionRecordRepository, ExecutionRecordRepository>();

        services.AddSingleton<IAutoClaudeAppSettings>(new AutoClaudeAppSettingsStore(settingsPath));

        services.AddSingleton<ICliExecutor, ClaudeCliExecutor>();

        services.AddSingleton<IPhaseHandler, AnalysisPhaseHandler>();
        services.AddSingleton<IPhaseHandler, DecompositionPhaseHandler>();
        services.AddSingleton<IPhaseHandler, SubtaskCreationPhaseHandler>();
        services.AddSingleton<IPhaseHandler, ExecutionPhaseHandler>();
        services.AddSingleton<IPhaseHandler, ValidationPhaseHandler>();
        services.AddSingleton<PhaseHandlerFactory>();

        services.AddSingleton<WorkModelSeeder>();
        services.AddSingleton<OrchestrationEngine>();
        services.AddSingleton<SessionService>();
    }
}
