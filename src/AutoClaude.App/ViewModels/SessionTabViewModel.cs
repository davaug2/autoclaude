using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace AutoClaude.App.ViewModels;

public sealed partial class SessionTabViewModel : ObservableObject
{
    [ObservableProperty]
    private string _logText = "";

    [ObservableProperty]
    private string _statusLine = "";

    [ObservableProperty]
    private string _userInput = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isInputVisible;

    [ObservableProperty]
    private string _inputPlaceholder = "Aguardando pergunta...";

    [ObservableProperty]
    private string _elapsedText = "";

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private string _cliInput = "";

    [ObservableProperty]
    private string _cliOutput = "";

    [ObservableProperty]
    private bool _isCliOutputVisible;

    [ObservableProperty]
    private bool _isDetailsVisible;

    public Guid SessionId { get; }
    public string SessionName { get; }
    public SessionDetailsViewModel? DetailsViewModel { get; set; }

    private DateTime? _executionStart;
    private DispatcherTimer? _timer;

    /// <summary>
    /// Set externally by the window to wire interrupt to the notifier.
    /// </summary>
    public IRelayCommand? InterruptCommand { get; set; }

    public event EventHandler? LogChanged;
    public event EventHandler? InputRequested;

    private TaskCompletionSource<string>? _currentAnswerTcs;
    private readonly Queue<PendingInteraction> _interactionQueue = new();

    public SessionTabViewModel(Guid sessionId, string sessionName)
    {
        SessionId = sessionId;
        SessionName = sessionName;
    }

    public event EventHandler? CliOutputChanged;

    public void AppendCliOutput(string text)
    {
        CliOutput += text;
        CliOutputChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetCliInput(string prompt)
    {
        CliInput = prompt;
    }

    public void ClearCliOutput()
    {
        CliOutput = "";
    }

    [RelayCommand]
    private void ToggleCliOutput()
    {
        IsCliOutputVisible = !IsCliOutputVisible;
    }

    public event EventHandler? DetailsToggled;

    [RelayCommand]
    private void ToggleDetails()
    {
        IsDetailsVisible = !IsDetailsVisible;
        DetailsToggled?.Invoke(this, EventArgs.Empty);
    }

    public void StartTimer(DispatcherQueue dispatcher)
    {
        _executionStart = DateTime.Now;
        ElapsedText = "0s";
        IsExecuting = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            if (_executionStart.HasValue)
                ElapsedText = $"{(DateTime.Now - _executionStart.Value).TotalSeconds:F0}s";
        };
        _timer.Start();
    }

    public void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
        _executionStart = null;
        IsExecuting = false;
        ElapsedText = "";
    }

    public void AppendLog(string line)
    {
        LogText += line + Environment.NewLine;
        LogChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetStatus(string line)
    {
        StatusLine = line;
    }

    public Task<string> AskQuestion(string question)
    {
        return EnqueueOrShow(question, "Digite sua resposta...");
    }

    public Task<string> AskDecision(string message, string[] options)
    {
        var formatted = message + Environment.NewLine +
            string.Join(Environment.NewLine, options.Select((o, i) => $"  {i + 1}. {o}"));
        return EnqueueOrShow(formatted, "Digite o numero da opcao...");
    }

    public Task<string> AskConfirmation(string title, string details)
    {
        var text = title + Environment.NewLine + details + Environment.NewLine +
            "  1. Confirmar" + Environment.NewLine +
            "  2. Modificar" + Environment.NewLine +
            "  3. Voltar fase anterior" + Environment.NewLine +
            "  4. Rejeitar";
        return EnqueueOrShow(text, "Digite o numero da opcao...");
    }

    public Task<string> AskInterruptInput()
    {
        AppendLog("Execucao interrompida pelo usuario.");
        return EnqueueOrShow("O que deseja fazer?", "Sua instrucao durante a interrupcao...");
    }

    private Task<string> EnqueueOrShow(string prompt, string placeholder)
    {
        var pending = new PendingInteraction(prompt, placeholder);

        if (_currentAnswerTcs != null && !_currentAnswerTcs.Task.IsCompleted)
        {
            _interactionQueue.Enqueue(pending);
            return pending.CompletionSource.Task;
        }

        ShowInteraction(pending);
        return pending.CompletionSource.Task;
    }

    private void ShowInteraction(PendingInteraction interaction)
    {
        AppendLog("");
        AppendLog($"--- {interaction.Prompt}");
        InputPlaceholder = interaction.Placeholder;
        IsInputVisible = true;
        _currentAnswerTcs = interaction.CompletionSource;
        InputRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanSend() => !string.IsNullOrWhiteSpace(UserInput);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void SendInput()
    {
        var text = UserInput.Trim();
        AppendLog($"> {text}");
        AppendLog("");
        UserInput = "";
        IsInputVisible = false;

        // Null out BEFORE TrySetResult to avoid race condition:
        // TrySetResult can run continuations inline (same SynchronizationContext),
        // which may call ShowInteraction and assign a NEW TCS to _currentAnswerTcs.
        // If we null after, we overwrite the new TCS and the system hangs.
        var tcs = _currentAnswerTcs;
        _currentAnswerTcs = null;
        tcs?.TrySetResult(text);

        // Process next queued interaction (only if inline continuation didn't already show one)
        if (_currentAnswerTcs == null && _interactionQueue.Count > 0)
        {
            ShowInteraction(_interactionQueue.Dequeue());
        }
    }

    partial void OnUserInputChanged(string value)
    {
        SendInputCommand.NotifyCanExecuteChanged();
    }

    private sealed class PendingInteraction
    {
        public string Prompt { get; }
        public string Placeholder { get; }
        public TaskCompletionSource<string> CompletionSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PendingInteraction(string prompt, string placeholder)
        {
            Prompt = prompt;
            Placeholder = placeholder;
        }
    }
}
