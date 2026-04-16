using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using Spectre.Console;

namespace AutoClaude.Cli;

public class ConsoleNotifier : IOrchestrationNotifier
{
    private Timer? _spinnerTimer;
    private int _spinnerFrame;
    private string _spinnerDescription = "";
    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    public Task OnPhaseStarted(Phase phase, Session session)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold blue]Fase {phase.Ordinal}: {phase.Name}[/]").LeftJustified());
        AnsiConsole.MarkupLine($"  [dim]Tipo:[/] {phase.PhaseType} | [dim]Modo:[/] {phase.RepeatMode}");
        if (!string.IsNullOrEmpty(phase.Description))
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(phase.Description)}[/]");
        return Task.CompletedTask;
    }

    public Task OnPhaseCompleted(Phase phase, bool success, string? errorMessage = null)
    {
        if (success)
            AnsiConsole.MarkupLine($"  [green]Fase '{Markup.Escape(phase.Name)}' concluída com sucesso.[/]");
        else
            AnsiConsole.MarkupLine($"  [red]Fase '{Markup.Escape(phase.Name)}' falhou: {Markup.Escape(errorMessage ?? "erro desconhecido")}[/]");
        return Task.CompletedTask;
    }

    public Task OnTaskStarted(TaskItem task)
    {
        AnsiConsole.MarkupLine($"  [yellow]>>> Tarefa {task.Ordinal}:[/] {Markup.Escape(task.Title)}");
        return Task.CompletedTask;
    }

    public Task OnSubtaskStarted(SubtaskItem subtask)
    {
        AnsiConsole.MarkupLine($"    [cyan]> Subtarefa {subtask.Ordinal}:[/] {Markup.Escape(subtask.Title)}");
        return Task.CompletedTask;
    }

    public Task OnExecutionStarted(string description)
    {
        _spinnerDescription = description;
        _spinnerFrame = 0;
        _spinnerTimer = new Timer(_ =>
        {
            var frame = SpinnerFrames[_spinnerFrame % SpinnerFrames.Length];
            var elapsed = (_spinnerFrame + 1) * 500 / 1000;
            Console.Write($"\r    {frame} [dim]{_spinnerDescription}[/] ({elapsed}s)   ");
            _spinnerFrame++;
        }, null, 0, 500);
        return Task.CompletedTask;
    }

    public Task OnCliOutputReceived(string line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            StopSpinner();
            AnsiConsole.MarkupLine($"    [dim]│[/] {Markup.Escape(line)}");
            RestartSpinner();
        }
        return Task.CompletedTask;
    }

    public Task OnExecutionCompleted(ExecutionRecord record)
    {
        StopSpinner();
        Console.Write("\r                                                                              \r");
        var color = record.Outcome == ExecutionOutcome.Success ? "green" : "red";
        var icon = record.Outcome == ExecutionOutcome.Success ? "✓" : "✗";
        AnsiConsole.MarkupLine($"    [{color}]{icon} {record.Outcome} ({record.DurationMs}ms)[/]");
        return Task.CompletedTask;
    }

    public Task<UserDecision> RequestUserDecision(string message, UserDecision[] options)
    {
        AnsiConsole.WriteLine();
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<UserDecision>()
                .Title(Markup.Escape(message))
                .AddChoices(options));
        return Task.FromResult(choice);
    }

    private void StopSpinner()
    {
        _spinnerTimer?.Dispose();
        _spinnerTimer = null;
    }

    private void RestartSpinner()
    {
        _spinnerTimer = new Timer(_ =>
        {
            var frame = SpinnerFrames[_spinnerFrame % SpinnerFrames.Length];
            var elapsed = (_spinnerFrame + 1) * 500 / 1000;
            Console.Write($"\r    {frame} {_spinnerDescription} ({elapsed}s)   ");
            _spinnerFrame++;
        }, null, 0, 500);
    }
}
