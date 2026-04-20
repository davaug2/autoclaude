using AutoClaude.Core.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoClaude.App.ViewModels.SessionEvents;

public abstract class SessionEventViewModel : ObservableObject
{
    public DateTime Timestamp { get; } = DateTime.Now;
    public string TimestampText => Timestamp.ToString("HH:mm:ss");
}

public sealed class PhaseStartedEventViewModel : SessionEventViewModel
{
    public int Ordinal { get; init; }
    public string Name { get; init; } = "";
    public PhaseType PhaseType { get; init; }
    public RepeatMode RepeatMode { get; init; }
    public string? Description { get; init; }

    public string PhaseTypeLabel => PhaseType switch
    {
        Core.Domain.Enums.PhaseType.Analysis => "Análise",
        Core.Domain.Enums.PhaseType.Decomposition => "Decomposição",
        Core.Domain.Enums.PhaseType.SubtaskCreation => "Subtarefas",
        Core.Domain.Enums.PhaseType.Execution => "Execução",
        Core.Domain.Enums.PhaseType.Validation => "Validação",
        _ => PhaseType.ToString()
    };

    public string RepeatModeLabel => RepeatMode switch
    {
        Core.Domain.Enums.RepeatMode.Once => "única",
        Core.Domain.Enums.RepeatMode.PerTask => "por tarefa",
        Core.Domain.Enums.RepeatMode.PerSubtask => "por subtarefa",
        _ => RepeatMode.ToString()
    };
}

public sealed partial class PhaseCompletedEventViewModel : SessionEventViewModel
{
    public string Name { get; init; } = "";
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public string Headline => Success ? $"Fase '{Name}' concluída" : $"Fase '{Name}' falhou";
}

public sealed class TaskStartedEventViewModel : SessionEventViewModel
{
    public int Ordinal { get; init; }
    public string Title { get; init; } = "";
    public string? Description { get; init; }
}

public sealed class SubtaskStartedEventViewModel : SessionEventViewModel
{
    public int Ordinal { get; init; }
    public string Title { get; init; } = "";
    public string? WorkingDirectory { get; init; }
}

public sealed partial class ExecutionEventViewModel : SessionEventViewModel
{
    [ObservableProperty] private ExecutionOutcome _outcome = ExecutionOutcome.Pending;
    [ObservableProperty] private long? _durationMs;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _cliOutputBuffer = "";
    [ObservableProperty] private bool _isCliOutputExpanded;

    public string? PromptPreview { get; init; }
    public int AttemptNumber { get; init; } = 1;
    public bool IsRetry => AttemptNumber > 1;
    public string AttemptLabel => AttemptNumber > 1 ? $"tentativa {AttemptNumber}" : "";

    public string DurationText => DurationMs.HasValue
        ? (DurationMs.Value >= 1000 ? $"{DurationMs.Value / 1000.0:F1}s" : $"{DurationMs.Value}ms")
        : "";

    partial void OnDurationMsChanged(long? value) => OnPropertyChanged(nameof(DurationText));

    public void AppendCliOutput(string text)
    {
        CliOutputBuffer += text;
    }
}

public sealed class RetryEventViewModel : SessionEventViewModel
{
    public int Attempt { get; init; }
    public int MaxAttempts { get; init; } = 3;
    public double DelaySeconds { get; init; }
    public string? Reason { get; init; }
}

public sealed partial class InterpretingIntentEventViewModel : SessionEventViewModel
{
    [ObservableProperty] private bool _isCompleted;

    public bool IsInProgress => !IsCompleted;

    partial void OnIsCompletedChanged(bool value) => OnPropertyChanged(nameof(IsInProgress));
}

public enum InfoSeverity
{
    Info,
    Warning,
    Error,
    Success
}

public sealed class InfoEventViewModel : SessionEventViewModel
{
    public string Message { get; init; } = "";
    public InfoSeverity Severity { get; init; } = InfoSeverity.Info;
}

public sealed partial class UserQuestionEventViewModel : SessionEventViewModel
{
    [ObservableProperty] private string _answer = "";
    [ObservableProperty] private bool _isAnswered;

    public string Prompt { get; init; } = "";
    internal TaskCompletionSource<string> Tcs { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public UserQuestionEventViewModel()
    {
        SubmitCommand = new RelayCommand(Submit, () => !IsAnswered && !string.IsNullOrWhiteSpace(Answer));
    }

    public IRelayCommand SubmitCommand { get; }

    partial void OnAnswerChanged(string value) => SubmitCommand.NotifyCanExecuteChanged();
    partial void OnIsAnsweredChanged(bool value) => SubmitCommand.NotifyCanExecuteChanged();

    private void Submit()
    {
        if (IsAnswered) return;
        var trimmed = Answer.Trim();
        Answer = trimmed;
        IsAnswered = true;
        Tcs.TrySetResult(trimmed);
    }
}

public sealed class DecisionOption
{
    public int Index { get; init; }
    public string Label { get; init; } = "";
    public UserDecision Value { get; init; }
    public IRelayCommand SelectCommand { get; init; } = new RelayCommand(() => { });
}

public sealed partial class UserDecisionEventViewModel : SessionEventViewModel
{
    [ObservableProperty] private string? _chosenLabel;
    [ObservableProperty] private bool _isAnswered;

    public string Prompt { get; init; } = "";
    public IReadOnlyList<DecisionOption> Options { get; private set; } = Array.Empty<DecisionOption>();
    internal TaskCompletionSource<UserDecision> Tcs { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void SetOptions(IEnumerable<(int index, string label, UserDecision value)> options)
    {
        var list = new List<DecisionOption>();
        foreach (var (index, label, value) in options)
        {
            var captured = (index, label, value);
            var opt = new DecisionOption
            {
                Index = captured.index,
                Label = captured.label,
                Value = captured.value,
                SelectCommand = new RelayCommand(
                    () => Choose(captured.value, captured.label),
                    () => !IsAnswered)
            };
            list.Add(opt);
        }
        Options = list;
    }

    partial void OnIsAnsweredChanged(bool value)
    {
        foreach (var opt in Options)
            opt.SelectCommand.NotifyCanExecuteChanged();
    }

    private void Choose(UserDecision value, string label)
    {
        if (IsAnswered) return;
        ChosenLabel = label;
        IsAnswered = true;
        Tcs.TrySetResult(value);
    }
}

public sealed partial class UserConfirmationEventViewModel : SessionEventViewModel
{
    [ObservableProperty] private string? _chosenLabel;
    [ObservableProperty] private bool _isAnswered;

    public string Title { get; init; } = "";
    public string Details { get; init; } = "";
    internal TaskCompletionSource<ConfirmationResult> Tcs { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public UserConfirmationEventViewModel()
    {
        ConfirmCommand = new RelayCommand(
            () => Choose(ConfirmationResult.Confirm, "Confirmado"),
            () => !IsAnswered);
        ModifyCommand = new RelayCommand(
            () => Choose(ConfirmationResult.Modify, "Modificar"),
            () => !IsAnswered);
        GoBackCommand = new RelayCommand(
            () => Choose(ConfirmationResult.GoBack, "Voltar fase anterior"),
            () => !IsAnswered);
        RejectCommand = new RelayCommand(
            () => Choose(ConfirmationResult.Reject, "Rejeitado"),
            () => !IsAnswered);
    }

    public IRelayCommand ConfirmCommand { get; }
    public IRelayCommand ModifyCommand { get; }
    public IRelayCommand GoBackCommand { get; }
    public IRelayCommand RejectCommand { get; }

    partial void OnIsAnsweredChanged(bool value)
    {
        ConfirmCommand.NotifyCanExecuteChanged();
        ModifyCommand.NotifyCanExecuteChanged();
        GoBackCommand.NotifyCanExecuteChanged();
        RejectCommand.NotifyCanExecuteChanged();
    }

    private void Choose(ConfirmationResult result, string label)
    {
        if (IsAnswered) return;
        ChosenLabel = label;
        IsAnswered = true;
        Tcs.TrySetResult(result);
    }
}
