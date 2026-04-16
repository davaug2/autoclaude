using System.Text.Json;
using AutoClaude.Cli.Rendering;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using AutoClaude.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AutoClaude.Cli.Commands;

public class DefaultCommand : AsyncCommand<EmptyCommandSettings>
{
    private const string NewSessionOption = "+ Nova sessao";

    private readonly SessionService _sessionService;
    private readonly ICliExecutor _cliExecutor;

    public DefaultCommand(SessionService sessionService, ICliExecutor cliExecutor)
    {
        _sessionService = sessionService;
        _cliExecutor = cliExecutor;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, EmptyCommandSettings settings, CancellationToken ct)
    {
        var sessions = await _sessionService.ListAsync();

        if (sessions.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold]AutoClaude — Sessoes[/]");
            AnsiConsole.WriteLine();
            SessionTableRenderer.Render(sessions);
            AnsiConsole.WriteLine();

            var choices = sessions
                .Select(s => $"{s.Id.ToString()[..8]} | {s.Status,-10} | {Truncate(s.Objective ?? "-", 50)}")
                .ToList();
            choices.Add(NewSessionOption);

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]Selecione uma sessao para retomar ou crie uma nova:[/]")
                    .PageSize(10)
                    .AddChoices(choices));

            if (selected == NewSessionOption)
                return await CreateAndRunNewSessionAsync(ct);

            var selectedId = Guid.Parse(sessions[choices.IndexOf(selected)].Id.ToString());
            return await ResumeSessionAsync(selectedId, ct);
        }

        AnsiConsole.MarkupLine("[bold]AutoClaude — Orquestrador de IA[/]");
        AnsiConsole.MarkupLine("[dim]Nenhuma sessao encontrada. Vamos criar uma![/]");
        AnsiConsole.WriteLine();

        return await CreateAndRunNewSessionAsync(ct);
    }

    private async Task<int> CreateAndRunNewSessionAsync(CancellationToken ct)
    {
        var objective = AnsiConsole.Ask<string>("[yellow]Qual o objetivo da sessao?[/]");

        if (string.IsNullOrWhiteSpace(objective))
        {
            AnsiConsole.MarkupLine("[red]Objetivo nao pode ser vazio.[/]");
            return 1;
        }

        // Detect directories from objective via AI
        var directories = await DetectDirectoriesAsync(objective, ct);

        if (directories.Count == 0)
        {
            var defaultPath = Directory.GetCurrentDirectory();
            var targetPath = AnsiConsole.Ask("[yellow]Caminho do projeto:[/]", defaultPath);
            if (Directory.Exists(targetPath))
                directories.Add(targetPath);
        }

        // Confirm directories with user
        if (directories.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold]Diretorios detectados:[/]");
            foreach (var dir in directories)
            {
                var exists = Directory.Exists(dir) ? "[green]OK[/]" : "[red]NAO ENCONTRADO[/]";
                AnsiConsole.MarkupLine($"  {Markup.Escape(dir)} {exists}");
            }
            AnsiConsole.WriteLine();

            var addMore = AnsiConsole.Confirm("[yellow]Deseja adicionar mais diretorios?[/]", false);
            if (addMore)
            {
                var extra = AnsiConsole.Ask<string>("[yellow]Caminho adicional:[/]");
                if (!string.IsNullOrWhiteSpace(extra) && Directory.Exists(extra))
                    directories.Add(extra);
            }

            directories = directories.Where(Directory.Exists).ToList();
        }

        var mainPath = directories.FirstOrDefault() ?? Directory.GetCurrentDirectory();
        var workModelId = await NewSessionCommand.SelectWorkModelInteractiveAsync(_sessionService);
        var session = await _sessionService.CreateAsync(objective, targetPath: mainPath, workModelId: workModelId);
        session.AllowedDirectories = directories;

        AnsiConsole.MarkupLine($"[green]Sessao criada:[/] {session.Id}");
        AnsiConsole.MarkupLine($"[dim]Diretorios:[/] {string.Join(", ", directories)}");
        AnsiConsole.MarkupLine($"[dim]Escrita permitida apenas na fase de execucao[/]");
        AnsiConsole.WriteLine();

        await _sessionService.RunAsync(session.Id, ct);
        return 0;
    }

    private async Task<List<string>> DetectDirectoriesAsync(string objective, CancellationToken ct)
    {
        try
        {
            var request = new CliRequest
            {
                Prompt = $"Analise o seguinte objetivo e extraia todos os caminhos de diretorios mencionados.\n\n" +
                         $"Objetivo: {objective}\n\n" +
                         "Retorne APENAS um JSON: {\"directories\": [\"caminho1\", \"caminho2\"]}\n" +
                         "Se nao houver caminhos mencionados, retorne {\"directories\": []}",
                TimeoutSeconds = 20
            };

            var result = await _cliExecutor.ExecuteAsync(request, ct);
            if (result.IsSuccess)
            {
                var text = ExtractResult(result.StandardOutput);
                return ParseDirectories(text);
            }
        }
        catch { }

        return new List<string>();
    }

    private static List<string> ParseDirectories(string text)
    {
        try
        {
            var jsonStart = text.IndexOf('{');
            var jsonEnd = text.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = text.Substring(jsonStart, jsonEnd - jsonStart + 1);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("directories", out var dirs) && dirs.ValueKind == JsonValueKind.Array)
                    return dirs.EnumerateArray().Select(d => d.GetString() ?? "").Where(d => !string.IsNullOrEmpty(d)).ToList();
            }
        }
        catch (JsonException) { }
        return new List<string>();
    }

    private static string ExtractResult(string jsonOutput)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonOutput);
            if (doc.RootElement.TryGetProperty("result", out var r))
                return r.GetString() ?? jsonOutput;
        }
        catch (JsonException) { }
        return jsonOutput;
    }

    private async Task<int> ResumeSessionAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            var session = await _sessionService.GetAsync(sessionId);
            AnsiConsole.MarkupLine($"[bold]Retomando sessao:[/] {session.Id}");
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
