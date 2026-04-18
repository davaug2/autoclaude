using System.ComponentModel;
using AutoClaude.Cli.Rendering;
using AutoClaude.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AutoClaude.Cli.Commands;

public class ResumeSessionCommand : AsyncCommand<ResumeSessionCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<SESSION_ID>")]
        [Description("ID da sessão para retomar")]
        public string SessionId { get; set; } = string.Empty;
    }

    private readonly SessionService _sessionService;

    public ResumeSessionCommand(SessionService sessionService)
    {
        _sessionService = sessionService;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        if (!Guid.TryParse(settings.SessionId, out var sessionId))
        {
            AnsiConsole.MarkupLine("[red]ID de sessão inválido.[/]");
            return 1;
        }

        try
        {
            var session = await _sessionService.GetAsync(sessionId);
            AnsiConsole.MarkupLine($"[bold]Retomando sessão:[/] {session.Id}");
            AnsiConsole.MarkupLine($"[dim]Objetivo:[/] {Markup.Escape(session.Objective ?? "-")}");
            AnsiConsole.MarkupLine($"[dim]Status:[/] {session.Status} | [dim]Fase atual:[/] {session.CurrentPhaseOrdinal}");
            AnsiConsole.WriteLine();
            SessionTableRenderer.WriteAllowedDirectoriesSection(session);

            await _sessionService.ResumeAsync(sessionId);
            return 0;
        }
        catch (KeyNotFoundException)
        {
            AnsiConsole.MarkupLine($"[red]Sessão {settings.SessionId} não encontrada.[/]");
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }
}
