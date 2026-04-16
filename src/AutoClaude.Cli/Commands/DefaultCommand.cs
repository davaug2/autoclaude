using AutoClaude.Cli.Rendering;
using AutoClaude.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AutoClaude.Cli.Commands;

public class DefaultCommand : AsyncCommand<EmptyCommandSettings>
{
    private readonly SessionService _sessionService;

    public DefaultCommand(SessionService sessionService)
    {
        _sessionService = sessionService;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, EmptyCommandSettings settings, CancellationToken ct)
    {
        var sessions = await _sessionService.ListAsync();

        if (sessions.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold]AutoClaude — Sessões recentes[/]");
            AnsiConsole.WriteLine();
            SessionTableRenderer.Render(sessions);
            return 0;
        }

        AnsiConsole.MarkupLine("[bold]AutoClaude — Orquestrador de IA[/]");
        AnsiConsole.MarkupLine("[dim]Nenhuma sessão encontrada. Vamos criar uma![/]");
        AnsiConsole.WriteLine();

        var objective = AnsiConsole.Ask<string>("[yellow]Qual o objetivo da sessão?[/]");

        if (string.IsNullOrWhiteSpace(objective))
        {
            AnsiConsole.MarkupLine("[red]Objetivo não pode ser vazio.[/]");
            return 1;
        }

        var targetPath = Directory.GetCurrentDirectory();
        var session = await _sessionService.CreateAsync(objective, targetPath: targetPath);

        AnsiConsole.MarkupLine($"[green]Sessão criada:[/] {session.Id}");
        AnsiConsole.WriteLine();

        await _sessionService.RunAsync(session.Id, ct);

        return 0;
    }
}
