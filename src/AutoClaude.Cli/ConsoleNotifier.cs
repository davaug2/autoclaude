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
    private CancellationTokenSource? _currentCts;

    public CancellationTokenSource? CurrentCts
    {
        get => _currentCts;
        set => _currentCts = value;
    }

    public ConsoleNotifier()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _currentCts?.Cancel();
        };
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
        AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(question)}[/]");
        AnsiConsole.MarkupLine("[dim]  (linha vazia para enviar)[/]");
        var editor = new ConsoleTextEditor(prefix: "    > ", multiLine: true);
        var answer = editor.Run() ?? "";
        return Task.FromResult(answer);
    }

    public Task<(Core.Domain.Enums.ConfirmationResult result, string? modification)> ConfirmWithUser(string title, string details)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(title)}[/]").LeftJustified());
        AnsiConsole.WriteLine(details);
        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]O que deseja fazer?[/]")
                .AddChoices("Confirmar", "Modificar", "Voltar fase anterior", "Rejeitar"));

        switch (choice)
        {
            case "Modificar":
                AnsiConsole.MarkupLine("[yellow]O que deseja modificar?[/]");
                AnsiConsole.MarkupLine("[dim]  (linha vazia para enviar)[/]");
                var modEditor = new ConsoleTextEditor(prefix: "    > ", multiLine: true);
                var modification = modEditor.Run() ?? "";
                return Task.FromResult<(Core.Domain.Enums.ConfirmationResult, string?)>(
                    (Core.Domain.Enums.ConfirmationResult.Modify, modification));
            case "Voltar fase anterior":
                return Task.FromResult<(Core.Domain.Enums.ConfirmationResult, string?)>(
                    (Core.Domain.Enums.ConfirmationResult.GoBack, null));
            case "Confirmar":
                return Task.FromResult<(Core.Domain.Enums.ConfirmationResult, string?)>(
                    (Core.Domain.Enums.ConfirmationResult.Confirm, null));
            default:
                return Task.FromResult<(Core.Domain.Enums.ConfirmationResult, string?)>(
                    (Core.Domain.Enums.ConfirmationResult.Reject, null));
        }
    }

    public CancellationToken CreateInterruptToken()
    {
        _currentCts = new CancellationTokenSource();
        return _currentCts.Token;
    }

    public void ResetInterruptToken()
    {
        _currentCts?.Dispose();
        _currentCts = new CancellationTokenSource();
    }

    public Task<string?> OnUserInterrupt()
    {
        lock (_consoleLock)
        {
            StopSpinner();
            ClearLine();
        }

        AnsiConsole.MarkupLine("[yellow]    Execucao interrompida (Ctrl+C)[/]");
        AnsiConsole.MarkupLine("[dim]    Ctrl+C novamente para cancelar[/]");
        var editor = new ConsoleTextEditor(prefix: "    > ", allowCtrlCCancel: true, multiLine: true);
        var input = editor.Run();
        return Task.FromResult(input);
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
