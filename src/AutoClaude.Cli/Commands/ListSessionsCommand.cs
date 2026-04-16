using AutoClaude.Cli.Rendering;
using AutoClaude.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AutoClaude.Cli.Commands;

public class ListSessionsCommand : AsyncCommand<EmptyCommandSettings>
{
    private readonly SessionService _sessionService;

    public ListSessionsCommand(SessionService sessionService)
    {
        _sessionService = sessionService;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, EmptyCommandSettings settings, CancellationToken ct)
    {
        var sessions = await _sessionService.ListAsync();

        if (sessions.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]Nenhuma sessão encontrada.[/]");
            return 0;
        }

        SessionTableRenderer.Render(sessions);
        return 0;
    }
}
