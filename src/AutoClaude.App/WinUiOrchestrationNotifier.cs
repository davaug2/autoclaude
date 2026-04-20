using System.Collections.Concurrent;
using AutoClaude.App.ViewModels;
using AutoClaude.App.ViewModels.SessionEvents;
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

    // Active execution card per tab (so CLI output streams into the right card)
    private readonly ConcurrentDictionary<SessionTabViewModel, ExecutionEventViewModel> _activeExecutions = new();

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
        {
            _interruptMap.TryRemove(tab, out _);
            _activeExecutions.TryRemove(tab, out _);
        }
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
            if (tab == null) return;
            tab.AddEvent(new PhaseStartedEventViewModel
            {
                Ordinal = phase.Ordinal,
                Name = phase.Name,
                PhaseType = phase.PhaseType,
                RepeatMode = phase.RepeatMode,
                Description = phase.Description
            });
            tab.CurrentPhaseName = phase.Name;
            tab.CurrentTaskTitle = null;
            tab.CurrentSubtaskTitle = null;
            tab.SetStatus($"Fase: {phase.Name}");
        });
        return Task.CompletedTask;
    }

    public Task OnPhaseCompleted(Phase phase, bool success, string? errorMessage = null)
    {
        var tab = _currentTab.Value;
        RunOnUi(() =>
        {
            tab?.AddEvent(new PhaseCompletedEventViewModel
            {
                Name = phase.Name,
                Success = success,
                ErrorMessage = errorMessage
            });
        });
        return Task.CompletedTask;
    }

    public Task OnTaskStarted(TaskItem task)
    {
        var tab = _currentTab.Value;
        RunOnUi(() =>
        {
            if (tab == null) return;
            tab.AddEvent(new TaskStartedEventViewModel
            {
                Ordinal = task.Ordinal,
                Title = task.Title,
                Description = task.Description
            });
            tab.CurrentTaskTitle = task.Title;
            tab.CurrentSubtaskTitle = null;
            tab.SetStatus($"Tarefa: {task.Title}");
        });
        return Task.CompletedTask;
    }

    public Task OnSubtaskStarted(SubtaskItem subtask)
    {
        var tab = _currentTab.Value;
        RunOnUi(() =>
        {
            if (tab == null) return;
            tab.AddEvent(new SubtaskStartedEventViewModel
            {
                Ordinal = subtask.Ordinal,
                Title = subtask.Title,
                WorkingDirectory = subtask.WorkingDirectory
            });
            tab.CurrentSubtaskTitle = subtask.Title;
            tab.SetStatus($"Subtarefa: {subtask.Title}");
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
            if (tab == null) return;

            var ev = new ExecutionEventViewModel
            {
                PromptPreview = prompt ?? description,
                AttemptNumber = 1
            };
            tab.AddEvent(ev);
            _activeExecutions[tab] = ev;

            tab.SetStatus(desc);
            if (_dispatcher != null)
                tab.StartTimer(_dispatcher);
        });
        return Task.CompletedTask;
    }

    public Task OnCliOutputReceived(string line)
    {
        if (string.IsNullOrEmpty(line))
            return Task.CompletedTask;

        var tab = _currentTab.Value;
        if (tab == null)
            return Task.CompletedTask;

        RunOnUi(() =>
        {
            if (_activeExecutions.TryGetValue(tab, out var ev))
                ev.AppendCliOutput(line);
            else
                tab.AddEvent(new InfoEventViewModel { Message = line, Severity = InfoSeverity.Info });
        });
        return Task.CompletedTask;
    }

    public Task OnRetryStarted(int attempt, TimeSpan delay, string? reason)
    {
        var tab = _currentTab.Value;
        RunOnUi(() =>
        {
            if (tab == null) return;
            tab.StopTimer();
            tab.AddEvent(new RetryEventViewModel
            {
                Attempt = attempt,
                DelaySeconds = delay.TotalSeconds,
                Reason = reason
            });
            var msg = string.IsNullOrWhiteSpace(reason)
                ? $"Tentativa {attempt}/3 em {delay.TotalSeconds:F0}s"
                : $"Falha: {reason} | Tentativa {attempt}/3 em {delay.TotalSeconds:F0}s";
            tab.SetStatus(msg);
        });
        return Task.CompletedTask;
    }

    public Task OnRetryExecuting(int attempt)
    {
        var tab = _currentTab.Value;
        var desc = _currentExecDesc.Value ?? "";
        RunOnUi(() =>
        {
            if (tab == null) return;

            var ev = new ExecutionEventViewModel
            {
                PromptPreview = desc,
                AttemptNumber = attempt
            };
            tab.AddEvent(ev);
            _activeExecutions[tab] = ev;

            tab.SetStatus($"Tentativa {attempt}/3: {desc}");
            if (_dispatcher != null)
                tab.StartTimer(_dispatcher);
        });
        return Task.CompletedTask;
    }

    public Task OnExecutionCompleted(ExecutionRecord record)
    {
        var tab = _currentTab.Value;
        RunOnUi(() =>
        {
            if (tab == null) return;

            if (_activeExecutions.TryRemove(tab, out var ev))
            {
                ev.Outcome = record.Outcome;
                ev.DurationMs = record.DurationMs;
                ev.ErrorMessage = record.ErrorMessage;
            }

            tab.StopTimer();
            tab.SetStatus("Execução concluída");
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

            var labeled = options.Select(o => (value: o, label: UserDecisionLabel(o))).ToList();
            return await tab.AskDecision(message, labeled);
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

            var pick = await tab.AskConfirmation(title, details);

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

            tab.AddEvent(new InfoEventViewModel
            {
                Message = "Execução interrompida pelo usuário.",
                Severity = InfoSeverity.Warning
            });
            var input = await tab.AskQuestion("O que deseja fazer?");
            var trimmed = input.Trim();
            return string.IsNullOrEmpty(trimmed) ? (string?)null : trimmed;
        });
    }

    public Task OnInterpretingUserIntentStarted()
    {
        var tab = _currentTab.Value;
        RunOnUi(() =>
        {
            tab?.AddEvent(new InterpretingIntentEventViewModel());
            tab?.SetStatus("Interpretando intenção...");
        });
        return Task.CompletedTask;
    }

    public Task OnInterpretingUserIntentCompleted()
    {
        var tab = _currentTab.Value;
        RunOnUi(() =>
        {
            if (tab == null) return;
            // Mark the most recent interpretation card (if any) as completed
            for (int i = tab.Events.Count - 1; i >= 0; i--)
            {
                if (tab.Events[i] is InterpretingIntentEventViewModel ev && !ev.IsCompleted)
                {
                    ev.IsCompleted = true;
                    break;
                }
            }
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
