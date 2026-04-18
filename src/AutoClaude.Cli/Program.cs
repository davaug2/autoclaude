using System.Text;
using AutoClaude.Cli;
using AutoClaude.Cli.Commands;
using AutoClaude.Cli.Infrastructure;
using AutoClaude.Core.PhaseHandlers;
using AutoClaude.Core.Ports;
using AutoClaude.Core.Services;
using AutoClaude.Infrastructure.Configuration;
using AutoClaude.Infrastructure.Data;
using AutoClaude.Infrastructure.Executors;
using AutoClaude.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
// Database path
var dbDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AutoClaude");
Directory.CreateDirectory(dbDir);
var dbPath = Path.Combine(dbDir, "autoclaude.db");
var settingsPath = Path.Combine(dbDir, "settings.json");
var connectionString = $"Data Source={dbPath}";

var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Warning);
});

// Infrastructure - Database
services.AddSingleton(new ConnectionFactory(connectionString));
services.AddSingleton<IDatabaseInitializer, SqliteDatabaseInitializer>();

// Infrastructure - Repositories
services.AddSingleton<IWorkModelRepository, WorkModelRepository>();
services.AddSingleton<IPhaseRepository, PhaseRepository>();
services.AddSingleton<ISessionRepository, SessionRepository>();
services.AddSingleton<ITaskRepository, TaskRepository>();
services.AddSingleton<ISubtaskRepository, SubtaskRepository>();
services.AddSingleton<IExecutionRecordRepository, ExecutionRecordRepository>();

// App settings (LocalApplicationData/AutoClaude/settings.json)
services.AddSingleton<IAutoClaudeAppSettings>(new AutoClaudeAppSettingsStore(settingsPath));

// Infrastructure - CLI Executor
services.AddSingleton<ICliExecutor, ClaudeCliExecutor>();

// Core - Phase Handlers
services.AddSingleton<IPhaseHandler, AnalysisPhaseHandler>();
services.AddSingleton<IPhaseHandler, DecompositionPhaseHandler>();
services.AddSingleton<IPhaseHandler, SubtaskCreationPhaseHandler>();
services.AddSingleton<IPhaseHandler, ExecutionPhaseHandler>();
services.AddSingleton<IPhaseHandler, ValidationPhaseHandler>();
services.AddSingleton<PhaseHandlerFactory>();

// Core - Services
services.AddSingleton<WorkModelSeeder>();
services.AddSingleton<OrchestrationEngine>();
services.AddSingleton<SessionService>();

// CLI - Notifier
services.AddSingleton<IOrchestrationNotifier, ConsoleNotifier>();

using (var bootstrap = services.BuildServiceProvider())
    await bootstrap.GetRequiredService<IDatabaseInitializer>().InitializeAsync();

// Spectre.Console.Cli
var registrar = new TypeRegistrar(services);
var app = new CommandApp<DefaultCommand>(registrar);

app.Configure(config =>
{
    config.SetApplicationName("autoclaude");

    config.AddCommand<NewSessionCommand>("new")
        .WithDescription("Criar e executar uma nova sessão de orquestração")
        .WithExample("new", "\"Criar uma API REST com autenticação\"");

    config.AddCommand<ListSessionsCommand>("list")
        .WithDescription("Listar todas as sessões");

    config.AddCommand<ResumeSessionCommand>("resume")
        .WithDescription("Retomar uma sessão pausada ou falhada")
        .WithExample("resume", "a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    config.AddCommand<StatusCommand>("status")
        .WithDescription("Mostrar status e progresso de uma sessão")
        .WithExample("status", "a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    config.AddCommand<SettingsCommand>("settings")
        .WithDescription("Exibir ou editar configuracoes (debug: linha de comando completa do Claude)")
        .WithExample("settings", "")
        .WithExample("settings", "debug on");
});

return await app.RunAsync(args);
