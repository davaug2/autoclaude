using System.Text.Json;
using AutoClaude.Cli.Input;
using AutoClaude.Cli.Rendering;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using AutoClaude.Core.Services;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AutoClaude.Cli.Commands;

public class DefaultCommand : AsyncCommand<EmptyCommandSettings>
{
    private const string NewSessionOption = "+ Nova sessao";
    private const string SettingsOption = "* Configuracoes";
    private const string DeleteSessionOption = "x Excluir sessao";

    private readonly SessionService _sessionService;
    private readonly ICliExecutor _cliExecutor;
    private readonly IAutoClaudeAppSettings _appSettings;
    private readonly ILogger<DefaultCommand> _logger;

    public DefaultCommand(
        SessionService sessionService,
        ICliExecutor cliExecutor,
        IAutoClaudeAppSettings appSettings,
        ILogger<DefaultCommand> logger)
    {
        _sessionService = sessionService;
        _cliExecutor = cliExecutor;
        _appSettings = appSettings;
        _logger = logger;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, EmptyCommandSettings settings, CancellationToken ct)
    {
        return await ShowMainScreenAsync(ct);
    }

    private async Task<int> ShowMainScreenAsync(CancellationToken ct)
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
            choices.Add(SettingsOption);
            choices.Add(DeleteSessionOption);

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]Selecione uma sessao para retomar ou crie uma nova:[/]")
                    .PageSize(12)
                    .AddChoices(choices));

            if (selected == NewSessionOption)
                return await CreateAndRunNewSessionAsync(ct);

            if (selected == SettingsOption)
            {
                AppSettingsCli.RunInteractive(_appSettings);
                return await ShowMainScreenAsync(ct);
            }

            if (selected == DeleteSessionOption)
            {
                await DeleteSessionAsync(sessions, ct);
                return await ShowMainScreenAsync(ct);
            }

            var selectedId = Guid.Parse(sessions[choices.IndexOf(selected)].Id.ToString());
            return await ResumeSessionAsync(selectedId, ct);
        }

        AnsiConsole.MarkupLine("[bold]AutoClaude — Orquestrador de IA[/]");
        AnsiConsole.MarkupLine("[dim]Nenhuma sessao encontrada.[/]");
        AnsiConsole.WriteLine();

        var startChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Escolha uma opcao:[/]")
                .AddChoices(NewSessionOption, SettingsOption));

        if (startChoice == SettingsOption)
        {
            AppSettingsCli.RunInteractive(_appSettings);
            return await ShowMainScreenAsync(ct);
        }

        return await CreateAndRunNewSessionAsync(ct);
    }

    private async Task<int> CreateAndRunNewSessionAsync(CancellationToken ct)
    {
        var objective = TextInputPrompt.Read("Qual o objetivo da sessao?", allowEmpty: false, allowCancel: true);
        if (objective == null)
        {
            AnsiConsole.MarkupLine("[dim]Operacao cancelada.[/]");
            return 1;
        }

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
            var targetPath = TextInputPrompt.Read("Caminho do projeto", initial: defaultPath, allowEmpty: false, allowCancel: false);
            if (!string.IsNullOrWhiteSpace(targetPath) && Directory.Exists(targetPath))
                directories.Add(targetPath!);
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
                var extra = TextInputPrompt.Read("Caminho adicional", allowEmpty: true, allowCancel: true);
                if (!string.IsNullOrWhiteSpace(extra) && Directory.Exists(extra!))
                    directories.Add(extra!);
            }

            directories = directories.Where(Directory.Exists).ToList();
        }

        var mainPath = directories.FirstOrDefault() ?? Directory.GetCurrentDirectory();
        var workModelId = await NewSessionCommand.SelectWorkModelInteractiveAsync(_sessionService);
        var session = await _sessionService.CreateAsync(objective, targetPath: mainPath, workModelId: workModelId);
        session.AllowedDirectories = directories;
        await _sessionService.PersistAllowedDirectoriesAsync(session, ct);

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
                         "Grave no arquivo de saida o JSON: {\"directories\": [\"caminho1\", \"caminho2\"]}\n" +
                         "Se nao houver caminhos mencionados, grave {\"directories\": []}",
                TimeoutSeconds = 20
            };

            var result = await _cliExecutor.ExecuteAsync(request, ct);
            if (result.IsSuccess)
            {
                if (!string.IsNullOrWhiteSpace(result.OutputJson))
                {
                    var fromFile = ParseDirectories(result.OutputJson);
                    if (fromFile.Count > 0) return fromFile;
                }
                var text = ExtractResult(result.StandardOutput);
                return ParseDirectories(text);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao detectar diretorios pelo objetivo");
        }

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

    private async Task<int> DeleteSessionAsync(IReadOnlyList<Session> sessions, CancellationToken ct)
    {
        var choices = sessions
            .Select(s => $"{s.Id.ToString()[..8]} | {Truncate(s.Objective ?? "-", 60)}")
            .ToList();

        var toDelete = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[red]Selecione as sessoes para excluir:[/]")
                .PageSize(10)
                .AddChoices(choices));

        if (toDelete.Count == 0) return 0;

        if (!AnsiConsole.Confirm($"[red]Excluir {toDelete.Count} sessao(oes)?[/]", false))
            return 0;

        foreach (var item in toDelete)
        {
            var index = choices.IndexOf(item);
            await _sessionService.DeleteAsync(sessions[index].Id);
            AnsiConsole.MarkupLine($"  [red]Excluida:[/] {item}");
        }

        return 0;
    }

    private async Task<int> ResumeSessionAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            var session = await _sessionService.GetAsync(sessionId);
            AnsiConsole.MarkupLine($"[bold]Retomando sessao:[/] {session.Id}");
            AnsiConsole.MarkupLine($"[dim]Objetivo:[/] {Markup.Escape(session.Objective ?? "-")}");
            AnsiConsole.WriteLine();
            SessionTableRenderer.WriteAllowedDirectoriesSection(session);

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
