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

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]ID[/]")
            .AddColumn("[bold]Nome[/]")
            .AddColumn("[bold]Objetivo[/]")
            .AddColumn("[bold]Status[/]")
            .AddColumn("[bold]Fase[/]")
            .AddColumn("[bold]Criada em[/]");

        foreach (var session in sessions)
        {
            var statusColor = session.Status switch
            {
                Core.Domain.Enums.SessionStatus.Completed => "green",
                Core.Domain.Enums.SessionStatus.Failed => "red",
                Core.Domain.Enums.SessionStatus.Running => "yellow",
                Core.Domain.Enums.SessionStatus.Paused => "blue",
                _ => "dim"
            };

            table.AddRow(
                session.Id.ToString()[..8],
                Markup.Escape(session.Name ?? "-"),
                Markup.Escape(Truncate(session.Objective ?? "-", 40)),
                $"[{statusColor}]{session.Status}[/]",
                session.CurrentPhaseOrdinal.ToString(),
                session.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }
}
