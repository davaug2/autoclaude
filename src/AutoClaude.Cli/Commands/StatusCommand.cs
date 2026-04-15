using System.ComponentModel;
using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AutoClaude.Cli.Commands;

public class StatusCommand : AsyncCommand<StatusCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<SESSION_ID>")]
        [Description("ID da sessão para visualizar status")]
        public string SessionId { get; set; } = string.Empty;
    }

    private readonly SessionService _sessionService;

    public StatusCommand(SessionService sessionService)
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
            var (tasks, subtasks) = await _sessionService.GetStatusAsync(sessionId);

            AnsiConsole.Write(new Rule($"[bold]Sessão: {Markup.Escape(session.Name ?? session.Id.ToString())}[/]"));
            AnsiConsole.MarkupLine($"  [dim]Objetivo:[/] {Markup.Escape(session.Objective ?? "-")}");
            AnsiConsole.MarkupLine($"  [dim]Status:[/] {session.Status}");
            AnsiConsole.MarkupLine($"  [dim]Fase atual:[/] {session.CurrentPhaseOrdinal}");
            AnsiConsole.WriteLine();

            if (tasks.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]  Nenhuma tarefa criada ainda.[/]");
                return 0;
            }

            var tree = new Tree("[bold]Tarefas[/]");

            foreach (var task in tasks)
            {
                var taskIcon = task.Status switch
                {
                    TaskItemStatus.Completed => "[green]✓[/]",
                    TaskItemStatus.Failed => "[red]✗[/]",
                    TaskItemStatus.InProgress => "[yellow]►[/]",
                    _ => "[dim]○[/]"
                };

                var taskNode = tree.AddNode($"{taskIcon} {Markup.Escape(task.Title)} [{GetStatusColor(task.Status)}]{task.Status}[/]");

                var taskSubtasks = subtasks.Where(s => s.TaskId == task.Id).OrderBy(s => s.Ordinal);
                foreach (var subtask in taskSubtasks)
                {
                    var subIcon = subtask.Status switch
                    {
                        SubtaskItemStatus.Completed => "[green]✓[/]",
                        SubtaskItemStatus.Failed => "[red]✗[/]",
                        SubtaskItemStatus.Running => "[yellow]►[/]",
                        SubtaskItemStatus.Skipped => "[dim]⊘[/]",
                        _ => "[dim]○[/]"
                    };

                    taskNode.AddNode($"{subIcon} {Markup.Escape(subtask.Title)} [{GetSubtaskStatusColor(subtask.Status)}]{subtask.Status}[/]");
                }
            }

            AnsiConsole.Write(tree);
            return 0;
        }
        catch (KeyNotFoundException)
        {
            AnsiConsole.MarkupLine($"[red]Sessão {settings.SessionId} não encontrada.[/]");
            return 1;
        }
    }

    private static string GetStatusColor(TaskItemStatus status) => status switch
    {
        TaskItemStatus.Completed => "green",
        TaskItemStatus.Failed => "red",
        TaskItemStatus.InProgress => "yellow",
        _ => "dim"
    };

    private static string GetSubtaskStatusColor(SubtaskItemStatus status) => status switch
    {
        SubtaskItemStatus.Completed => "green",
        SubtaskItemStatus.Failed => "red",
        SubtaskItemStatus.Running => "yellow",
        SubtaskItemStatus.Skipped => "dim",
        _ => "dim"
    };
}
