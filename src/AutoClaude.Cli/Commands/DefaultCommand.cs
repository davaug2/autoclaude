using AutoClaude.Cli.Rendering;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AutoClaude.Cli.Commands;

public class DefaultCommand : AsyncCommand<EmptyCommandSettings>
{
    private const string NewSessionOption = "✚ Nova sessão";

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
            AnsiConsole.MarkupLine("[bold]AutoClaude — Sessões[/]");
            AnsiConsole.WriteLine();
            SessionTableRenderer.Render(sessions);
            AnsiConsole.WriteLine();

            var choices = sessions
                .Select(s => $"{s.Id.ToString()[..8]} | {s.Status,-10} | {Truncate(s.Objective ?? "-", 50)}")
                .ToList();
            choices.Add(NewSessionOption);

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]Selecione uma sessão para retomar ou crie uma nova:[/]")
                    .PageSize(10)
                    .AddChoices(choices));

            if (selected == NewSessionOption)
                return await CreateAndRunNewSessionAsync(ct);

            var selectedId = Guid.Parse(sessions[choices.IndexOf(selected)].Id.ToString());
            return await ResumeSessionAsync(selectedId, ct);
        }

        AnsiConsole.MarkupLine("[bold]AutoClaude — Orquestrador de IA[/]");
        AnsiConsole.MarkupLine("[dim]Nenhuma sessão encontrada. Vamos criar uma![/]");
        AnsiConsole.WriteLine();

        return await CreateAndRunNewSessionAsync(ct);
    }

    private async Task<int> CreateAndRunNewSessionAsync(CancellationToken ct)
    {
        var objective = AnsiConsole.Ask<string>("[yellow]Qual o objetivo da sessão?[/]");

        if (string.IsNullOrWhiteSpace(objective))
        {
            AnsiConsole.MarkupLine("[red]Objetivo não pode ser vazio.[/]");
            return 1;
        }

        var workModelId = await NewSessionCommand.SelectWorkModelInteractiveAsync(_sessionService);
        var targetPath = Directory.GetCurrentDirectory();
        var session = await _sessionService.CreateAsync(objective, targetPath: targetPath, workModelId: workModelId);

        AnsiConsole.MarkupLine($"[green]Sessão criada:[/] {session.Id}");
        AnsiConsole.WriteLine();

        await _sessionService.RunAsync(session.Id, ct);
        return 0;
    }

    private async Task<int> ResumeSessionAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            var session = await _sessionService.GetAsync(sessionId);
            AnsiConsole.MarkupLine($"[bold]Retomando sessão:[/] {session.Id}");
            AnsiConsole.MarkupLine($"[dim]Objetivo:[/] {Markup.Escape(session.Objective ?? "-")}");
            AnsiConsole.WriteLine();

            await _sessionService.ResumeAsync(sessionId, ct);
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }
}
