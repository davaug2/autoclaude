using System.Text;
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
    private string _lastOutputLine = "";
    private static readonly string[] SpinnerFrames = ["|", "/", "-", "\\"];
    private readonly object _consoleLock = new();

    public ConsoleNotifier()
    {
        Console.OutputEncoding = Encoding.UTF8;
    }

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
            AnsiConsole.MarkupLine($"  [green]Fase '{Markup.Escape(phase.Name)}' concluida com sucesso.[/]");
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
        lock (_consoleLock)
        {
            _spinnerDescription = description.Length > 50 ? description[..47] + "..." : description;
            _spinnerTicks = 0;
            _lastOutputLine = "";
            StartSpinner();
        }
        return Task.CompletedTask;
    }

    public Task OnCliOutputReceived(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Task.CompletedTask;

        lock (_consoleLock)
        {
            if (text.Contains('\n'))
            {
                var lines = text.Split('\n');
                var last = lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "";
                _lastOutputLine = last.TrimEnd('\r');
            }
            else
            {
                _lastOutputLine += text;
            }
        }
        return Task.CompletedTask;
    }

    public Task OnExecutionCompleted(ExecutionRecord record)
    {
        lock (_consoleLock)
        {
            StopSpinner();
            ClearLine();
            var color = record.Outcome == ExecutionOutcome.Success ? "green" : "red";
            var icon = record.Outcome == ExecutionOutcome.Success ? "+" : "x";
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

    public Task<string> AskUserTextInput(string question)
    {
        AnsiConsole.WriteLine();
        var answer = AnsiConsole.Ask<string>($"[yellow]{Markup.Escape(question)}[/]");
        return Task.FromResult(answer);
    }

    public Task<bool> ConfirmWithUser(string title, string details)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(title)}[/]").LeftJustified());
        AnsiConsole.WriteLine(details);
        AnsiConsole.WriteLine();
        return Task.FromResult(AnsiConsole.Confirm("[yellow]Confirmar?[/]"));
    }

    private void StartSpinner()
    {
        _spinnerTimer = new Timer(_ =>
        {
            lock (_consoleLock)
            {
                var frame = SpinnerFrames[_spinnerTicks % SpinnerFrames.Length];
                var elapsed = (_spinnerTicks + 1) / 2;

                var output = !string.IsNullOrEmpty(_lastOutputLine)
                    ? Truncate(_lastOutputLine, 60)
                    : _spinnerDescription;

                var line = $"    {frame} {output} ({elapsed}s)";
                var width = GetWidth();
                var padding = Math.Max(0, width - line.Length - 1);
                Console.Write($"\r{line}{new string(' ', padding)}");
                _spinnerTicks++;
            }
        }, null, 0, 500);
    }

    private void StopSpinner()
    {
        _spinnerTimer?.Dispose();
        _spinnerTimer = null;
    }

    private static void ClearLine()
    {
        var width = GetWidth();
        Console.Write($"\r{new string(' ', width - 1)}\r");
    }

    private static int GetWidth()
    {
        try { return Console.WindowWidth; }
        catch { return 80; }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (maxLength <= 3) return text.Length <= maxLength ? text : "...";
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }
}
