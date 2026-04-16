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

        [CommandOption("-m|--model")]
        [Description("Nome do work model (padrão: seleção interativa)")]
        public string? WorkModel { get; set; }
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

        var workModelId = await ResolveWorkModelAsync(settings.WorkModel);

        var session = await _sessionService.CreateAsync(
            settings.Objective,
            name: settings.Name,
            targetPath: targetPath,
            workModelId: workModelId);

        AnsiConsole.MarkupLine($"[green]Sessão criada:[/] {session.Id}");
        AnsiConsole.WriteLine();

        await _sessionService.RunAsync(session.Id, ct);

        return 0;
    }

    private async Task<Guid?> ResolveWorkModelAsync(string? modelName)
    {
        if (!string.IsNullOrEmpty(modelName))
        {
            var models = await _sessionService.ListWorkModelsAsync();
            var match = models.FirstOrDefault(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.Id;
            AnsiConsole.MarkupLine($"[yellow]Work model '{Markup.Escape(modelName)}' não encontrado. Usando seleção interativa.[/]");
        }

        return await SelectWorkModelInteractiveAsync();
    }

    internal static async Task<Guid?> SelectWorkModelInteractiveAsync(SessionService sessionService)
    {
        var models = await sessionService.ListWorkModelsAsync();
        if (models.Count <= 1) return null;

        var choices = models.Select(m =>
        {
            var tag = m.IsBuiltin ? "[dim](builtin)[/]" : "[dim](custom)[/]";
            return $"{m.Name} {tag} - {Markup.Escape(m.Description ?? "")}";
        }).ToList();

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Selecione o modelo de trabalho:[/]")
                .AddChoices(choices));

        var index = choices.IndexOf(selected);
        return models[index].Id;
    }

    private async Task<Guid?> SelectWorkModelInteractiveAsync()
    {
        return await SelectWorkModelInteractiveAsync(_sessionService);
    }
}
