using System.Collections.Concurrent;
using AutoClaude.App.ViewModels;
using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace AutoClaude.App;

public sealed class WinUiOrchestrationNotifier : IOrchestrationNotifier
{
    private Window? _window;
    private DispatcherQueue? _dispatcher;

    // Per-session state via AsyncLocal — each async orchestration chain sees its own values
    private static readonly AsyncLocal<SessionTabViewModel?> _currentTab = new();
    private static readonly AsyncLocal<string?> _currentExecDesc = new();

    // Interrupt CTS keyed by tab — needed because RequestInterrupt is called from UI, not from the async context
    private readonly ConcurrentDictionary<SessionTabViewModel, CancellationTokenSource> _interruptMap = new();

    public void Attach(Window window)
    {
        _window = window;
        _dispatcher = window.DispatcherQueue;
    }

    public void SetActiveTab(SessionTabViewModel tab) => _currentTab.Value = tab;

    public void ClearActiveTab()
    {
        var tab = _currentTab.Value;
        if (tab != null)
            _interruptMap.TryRemove(tab, out _);
        _currentTab.Value = null;
    }

    public void RequestInterrupt(SessionTabViewModel tab)
    {
        if (_interruptMap.TryGetValue(tab, out var cts))
            cts.Cancel();
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher == null) return;
        if (_dispatcher.HasThreadAccess)
            action();
        else
            _dispatcher.TryEnqueue(() => action());
    }

    private async Task<T> RunOnUiAsync<T>(Func<Task<T>> action)
    {
        if (_dispatcher == null)
            throw new InvalidOperationException("WinUI not attached.");

        if (_dispatcher.HasThreadAccess)
            return await action();

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.TryEnqueue(() =>
        {
            _ = RunInnerAsync();
            async Task RunInnerAsync()
            {
                try
                {
                    var result = await action();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }
        });
        return await tcs.Task;
    }

    public Task OnPhaseStarted(Phase phase, Session session)
    {
        var tab = _currentTab.Value;
        RunOnUi(() =>
        {
            tab?.AppendLog("");
            tab?.AppendLog($"Fase {phase.Ordinal}: {phase.Name} ({phase.PhaseType}, {phase.RepeatMode})");
            if (!string.IsNullOrEmpty(phase.Description))
                tab?.AppendLog(phase.Description);
            tab?.SetStatus($"Fase: {phase.Name}");
        });
        return Task.CompletedTask;
    }

    public Task OnPhaseCompleted(Phase phase, bool success, string? errorMessage = null)
    {
        var tab = _currentTab.Value;
        RunOnUi(() =>
        {
            if (success)
            {
                tab?.AppendLog($"Fase '{phase.Name}' concluida.");
            }
            else
            {
                var msg = string.IsNullOrWhiteSpace(errorMessage) ? "erro desconhecido" : errorMessage;
                tab?.AppendLog($"Fase '{phase.Name}' falhou: {msg}");
            }
        });
        return Task.CompletedTask;
    }

    public Task OnTaskStarted(TaskItem task)
    {
        var tab = _currentTab.Value;
        RunOnUi(() =>
        {
            tab?.AppendLog($">>> Tarefa {task.Ordinal}: {task.Title}");
            tab?.SetStatus($"Tarefa: {task.Title}");
        });
        return Task.CompletedTask;
    }

    public Task OnSubtaskStarted(SubtaskItem subtask)
    {
        var tab = _currentTab.Value;
        RunOnUi(() =>
        {
            var subtaskLog = $"  > Subtarefa {subtask.Ordinal}: {subtask.Title}";
            if (!string.IsNullOrEmpty(subtask.WorkingDirectory))
                subtaskLog += $"\n    Diretorio: {subtask.WorkingDirectory}";
            tab?.AppendLog(subtaskLog);
            tab?.SetStatus($"Subtarefa: {subtask.Title}");
        });
        return Task.CompletedTask;
    }

    public Task OnExecutionStarted(string description, string? prompt = null)
    {
        var desc = description.Length > 80 ? description[..77] + "..." : description;
        _currentExecDesc.Value = desc;

        var tab = _currentTab.Value;
        RunOnUi(() =>
        {
            tab?.ClearCliOutput();
            if (prompt != null)
                tab?.SetCliInput(prompt);
            tab?.AppendLog($"Inicio: {DateTime.Now:HH:mm:ss}");
            tab?.SetStatus(desc);
            if (_dispatcher != null)
                tab?.StartTimer(_dispatcher);
        });
        return Task.CompletedTask;
    }

    public Task OnCliOutputReceived(string line)
    {
        if (string.IsNullOrEmpty(line))
            return Task.CompletedTask;

        var tab = _currentTab.Value;
        RunOnUi(() => tab?.AppendCliOutput(line));
        return Task.CompletedTask;
    }

    public Task OnRetryStarted(int attempt, TimeSpan delay, string? reason)
    {
        var tab = _currentTab.Value;
        var msg = string.IsNullOrWhiteSpace(reason)
            ? $"Tentativa {attempt}/3 em {delay.TotalSeconds:F0}s"
            : $"Falha: {reason} | Tentativa {attempt}/3 em {delay.TotalSeconds:F0}s";
        RunOnUi(() =>
        {
            tab?.StopTimer();
            tab?.SetStatus(msg);
        });
        return Task.CompletedTask;
    }

    public Task OnRetryExecuting(int attempt)
    {
        var tab = _currentTab.Value;
        var desc = _currentExecDesc.Value ?? "";
        RunOnUi(() =>
        {
            tab?.ClearCliOutput();
            tab?.AppendLog($"Tentativa {attempt}/3 iniciando...");
            tab?.SetStatus($"Tentativa {attempt}/3: {desc}");
            if (_dispatcher != null)
                tab?.StartTimer(_dispatcher);
        });
        return Task.CompletedTask;
    }

    public Task OnExecutionCompleted(ExecutionRecord record)
    {
        var tab = _currentTab.Value;
        RunOnUi(() =>
        {
            var seconds = record.DurationMs.HasValue ? $"{record.DurationMs.Value / 1000.0:F1}s" : "?";
            var startTime = record.StartedAt?.ToString("HH:mm:ss") ?? "?";
            var endTime = record.CompletedAt?.ToString("HH:mm:ss") ?? DateTime.Now.ToString("HH:mm:ss");
            tab?.StopTimer();
            tab?.SetStatus("Execucao concluida");
            tab?.AppendLog($"{record.Outcome} | {startTime} -> {endTime} ({seconds})");
            if (record.Outcome != ExecutionOutcome.Success && !string.IsNullOrWhiteSpace(record.ErrorMessage))
                tab?.AppendLog(record.ErrorMessage);
        });
        return Task.CompletedTask;
    }

    public Task<UserDecision> RequestUserDecision(string message, UserDecision[] options)
    {
        var tab = _currentTab.Value;
        return RunOnUiAsync(async () =>
        {
            if (tab == null)
                return options[0];

            var labels = options.Select(UserDecisionLabel).ToArray();
            var answer = await tab.AskDecision(message, labels);

            if (int.TryParse(answer.Trim(), out var idx) && idx >= 1 && idx <= options.Length)
                return options[idx - 1];

            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i].Equals(answer.Trim(), StringComparison.OrdinalIgnoreCase))
                    return options[i];
            }

            return options[0];
        });
    }

    private static string UserDecisionLabel(UserDecision d) => d switch
    {
        UserDecision.Continue => "Continuar",
        UserDecision.Retry => "Tentar novamente",
        UserDecision.Pause => "Pausar",
        UserDecision.Abort => "Abortar",
        UserDecision.Edit => "Editar",
        _ => d.ToString()
    };

    public Task<string> AskUserTextInput(string question)
    {
        var tab = _currentTab.Value;
        return RunOnUiAsync(async () =>
        {
            if (tab == null)
                return "";

            return await tab.AskQuestion(question);
        });
    }

    public Task<(ConfirmationResult result, string? modification)> ConfirmWithUser(string title, string details)
    {
        var tab = _currentTab.Value;
        return RunOnUiAsync(async () =>
        {
            if (tab == null)
                return (ConfirmationResult.Reject, (string?)null);

            var answer = await tab.AskConfirmation(title, details);

            var pick = answer.Trim() switch
            {
                "1" => ConfirmationResult.Confirm,
                "2" => ConfirmationResult.Modify,
                "3" => ConfirmationResult.GoBack,
                "4" => ConfirmationResult.Reject,
                _ => ConfirmationResult.Reject
            };

            if (pick != ConfirmationResult.Modify)
                return (pick, (string?)null);

            var modText = await tab.AskQuestion("O que deseja modificar?");
            var trimmed = modText.Trim();
            return string.IsNullOrEmpty(trimmed)
                ? (ConfirmationResult.Reject, (string?)null)
                : (ConfirmationResult.Modify, (string?)trimmed);
        });
    }

    public Task<string?> OnUserInterrupt()
    {
        var tab = _currentTab.Value;
        return RunOnUiAsync(async () =>
        {
            if (tab == null)
                return (string?)null;

            var input = await tab.AskInterruptInput();
            var trimmed = input.Trim();
            return string.IsNullOrEmpty(trimmed) ? (string?)null : trimmed;
        });
    }

    public Task OnInterpretingUserIntentStarted()
    {
        var tab = _currentTab.Value;
        RunOnUi(() =>
        {
            tab?.AppendLog("Analisando sessão");
            tab?.SetStatus("Interpretando intencao...");
        });
        return Task.CompletedTask;
    }

    public Task OnInterpretingUserIntentCompleted()
    {
        var tab = _currentTab.Value;
        RunOnUi(() =>
        {
            tab?.AppendLog("Intencao interpretada.");
        });
        return Task.CompletedTask;
    }

    public CancellationToken CreateInterruptToken()
    {
        var tab = _currentTab.Value;
        var cts = new CancellationTokenSource();
        if (tab != null)
            _interruptMap[tab] = cts;
        return cts.Token;
    }

    public void ResetInterruptToken()
    {
        var tab = _currentTab.Value;
        if (tab != null && _interruptMap.TryGetValue(tab, out var old))
            old.Dispose();
        var cts = new CancellationTokenSource();
        if (tab != null)
            _interruptMap[tab] = cts;
    }

    // Legacy parameterless — kept for interface, but prefer RequestInterrupt(tab)
    public void RequestInterrupt()
    {
        var tab = _currentTab.Value;
        if (tab != null)
            RequestInterrupt(tab);
    }
}
