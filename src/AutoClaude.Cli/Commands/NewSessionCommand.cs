using System.ComponentModel;
using AutoClaude.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AutoClaude.Cli.Commands;

public class NewSessionCommand : AsyncCommand<NewSessionCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<OBJECTIVE>")]
        [Description("Objetivo da sessão de orquestração")]
        public string Objective { get; set; } = string.Empty;

        [CommandOption("-n|--name")]
        [Description("Nome da sessão (opcional)")]
        public string? Name { get; set; }

        [CommandOption("-p|--path")]
        [Description("Caminho do projeto alvo")]
        public string? TargetPath { get; set; }
    }

    private readonly SessionService _sessionService;

    public NewSessionCommand(SessionService sessionService)
    {
        _sessionService = sessionService;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        var targetPath = settings.TargetPath ?? Directory.GetCurrentDirectory();

        AnsiConsole.MarkupLine("[bold]AutoClaude - Orquestrador de IA[/]");
        AnsiConsole.MarkupLine($"[dim]Objetivo:[/] {Markup.Escape(settings.Objective)}");
        AnsiConsole.MarkupLine($"[dim]Caminho:[/] {Markup.Escape(targetPath)}");
        AnsiConsole.WriteLine();

        var session = await _sessionService.CreateAsync(
            settings.Objective,
            name: settings.Name,
            targetPath: targetPath);

        AnsiConsole.MarkupLine($"[green]Sessão criada:[/] {session.Id}");
        AnsiConsole.WriteLine();

        await _sessionService.RunAsync(session.Id);

        return 0;
    }
}
