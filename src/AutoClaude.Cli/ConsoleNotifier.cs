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
    private readonly List<string> _recentLines = new();
    private readonly StringBuilder _currentLine = new();
    private static readonly string[] SpinnerFrames = ["|", "/", "-", "\\"];
    private readonly object _consoleLock = new();
    private int _displayLines;

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
            _recentLines.Clear();
            _currentLine.Clear();
            _displayLines = 0;
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
            _currentLine.Append(text);
            var current = _currentLine.ToString();

            if (current.Contains('\n'))
            {
                var parts = current.Split('\n');
                for (var i = 0; i < parts.Length - 1; i++)
                {
                    var line = parts[i].TrimEnd('\r');
                    if (!string.IsNullOrWhiteSpace(line))
                        _recentLines.Add(Truncate(line, Console.WindowWidth - 10));
                }
                _currentLine.Clear();
                _currentLine.Append(parts[^1]);

                while (_recentLines.Count > 3)
                    _recentLines.RemoveAt(0);
            }
        }
        return Task.CompletedTask;
    }

    public Task OnExecutionCompleted(ExecutionRecord record)
    {
        lock (_consoleLock)
        {
            StopSpinner();
            ClearDisplayArea();

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
        _spinnerTimer = new Timer(_ => RenderStatus(), null, 0, 500);
    }

    private void RenderStatus()
    {
        lock (_consoleLock)
        {
            ClearDisplayArea();

            var frame = SpinnerFrames[_spinnerTicks % SpinnerFrames.Length];
            var elapsed = (_spinnerTicks + 1) / 2;
            Console.WriteLine($"    {frame} {_spinnerDescription} ({elapsed}s)");
            var lines = 1;

            foreach (var line in _recentLines)
            {
                Console.WriteLine($"      {line}");
                lines++;
            }

            if (_currentLine.Length > 0)
            {
                var partial = Truncate(_currentLine.ToString().TrimEnd(), Console.WindowWidth - 10);
                if (!string.IsNullOrWhiteSpace(partial))
                {
                    Console.Write($"      {partial}");
                    lines++;
                }
            }

            _displayLines = lines;
            _spinnerTicks++;
        }
    }

    private void ClearDisplayArea()
    {
        if (_displayLines > 0)
        {
            try
            {
                var width = Console.WindowWidth;
                var blank = new string(' ', width - 1);
                for (var i = 0; i < _displayLines; i++)
                {
                    Console.SetCursorPosition(0, Console.CursorTop - (_displayLines > 1 && i == 0 ? 0 : 0));
                    Console.Write($"\r{blank}\r");
                    if (i < _displayLines - 1)
                    {
                        Console.CursorTop--;
                    }
                }
                Console.Write($"\r{blank}\r");
            }
            catch
            {
                Console.Write("\r                                                                        \r");
            }
            _displayLines = 0;
        }
    }

    private void StopSpinner()
    {
        _spinnerTimer?.Dispose();
        _spinnerTimer = null;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (maxLength <= 3) return text.Length <= maxLength ? text : "...";
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }
}
