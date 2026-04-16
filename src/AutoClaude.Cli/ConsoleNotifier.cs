using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using Spectre.Console;

namespace AutoClaude.Cli;

public class ConsoleNotifier : IOrchestrationNotifier
{
    private Timer? _spinnerTimer;
    private int _spinnerTicks;
    private string _spinnerDescription = "";
    private static readonly string[] SpinnerFrames = ["|", "/", "-", "\\"];
    private readonly object _consoleLock = new();

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
        _spinnerDescription = description.Length > 60 ? description[..57] + "..." : description;
        _spinnerTicks = 0;

        _spinnerTimer = new Timer(_ =>
        {
            lock (_consoleLock)
            {
                var frame = SpinnerFrames[_spinnerTicks % SpinnerFrames.Length];
                var elapsed = (_spinnerTicks + 1) / 2;
                var line = $"    {frame} {_spinnerDescription} ({elapsed}s)";
                var padding = new string(' ', Math.Max(0, Console.WindowWidth - line.Length - 1));
                Console.Write($"\r{line}{padding}");
                _spinnerTicks++;
            }
        }, null, 0, 500);

        return Task.CompletedTask;
    }

    private bool _outputLineStarted;

    public Task OnCliOutputReceived(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Task.CompletedTask;

        lock (_consoleLock)
        {
            StopSpinner();

            if (!_outputLineStarted)
            {
                ClearCurrentLine();
                Console.Write("    | ");
                _outputLineStarted = true;
            }

            if (text.Contains('\n'))
            {
                var parts = text.Split('\n');
                for (var i = 0; i < parts.Length; i++)
                {
                    Console.Write(parts[i]);
                    if (i < parts.Length - 1)
                    {
                        Console.WriteLine();
                        if (!string.IsNullOrWhiteSpace(parts[i + 1]))
                            Console.Write("    | ");
                    }
                }
            }
            else
            {
                Console.Write(text);
            }

            RestartSpinner();
        }
        return Task.CompletedTask;
    }

    public Task OnExecutionCompleted(ExecutionRecord record)
    {
        lock (_consoleLock)
        {
            StopSpinner();
            if (_outputLineStarted)
            {
                Console.WriteLine();
                _outputLineStarted = false;
            }
            ClearCurrentLine();
            var color = record.Outcome == ExecutionOutcome.Success ? "green" : "red";
            var icon = record.Outcome == ExecutionOutcome.Success ? "✓" : "✗";
            var seconds = record.DurationMs.HasValue ? $"{record.DurationMs.Value / 1000.0:F1}s" : "?";
            AnsiConsole.MarkupLine($"    [{color}]{icon} {record.Outcome} ({seconds})[/]");
        }
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
            lock (_consoleLock)
            {
                var frame = SpinnerFrames[_spinnerTicks % SpinnerFrames.Length];
                var elapsed = (_spinnerTicks + 1) / 2;
                var line = $"    {frame} {_spinnerDescription} ({elapsed}s)";
                var padding = new string(' ', Math.Max(0, Console.WindowWidth - line.Length - 1));
                Console.Write($"\r{line}{padding}");
                _spinnerTicks++;
            }
        }, null, 0, 500);
    }

    private static void ClearCurrentLine()
    {
        Console.Write($"\r{new string(' ', Console.WindowWidth - 1)}\r");
    }
}
