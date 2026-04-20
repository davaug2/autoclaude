using System.Collections.ObjectModel;
using AutoClaude.App.ViewModels.SessionEvents;
using AutoClaude.Core.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace AutoClaude.App.ViewModels;

public sealed partial class SessionTabViewModel : ObservableObject
{
    [ObservableProperty] private string _statusLine = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _elapsedText = "";
    [ObservableProperty] private bool _isExecuting;
    [ObservableProperty] private bool _isDetailsVisible;

    [ObservableProperty] private string? _currentPhaseName;
    [ObservableProperty] private string? _currentTaskTitle;
    [ObservableProperty] private string? _currentSubtaskTitle;

    public ObservableCollection<SessionEventViewModel> Events { get; } = new();

    public Guid SessionId { get; }
    public string SessionName { get; }
    public SessionDetailsViewModel? DetailsViewModel { get; set; }

    private DateTime? _executionStart;
    private DispatcherTimer? _timer;

    /// <summary>
    /// Set externally by the window to wire interrupt to the notifier.
    /// </summary>
    public IRelayCommand? InterruptCommand { get; set; }

    public event EventHandler? EventAdded;
    public event EventHandler? InputRequested;
    public event EventHandler? DetailsToggled;

    public SessionTabViewModel(Guid sessionId, string sessionName)
    {
        SessionId = sessionId;
        SessionName = sessionName;
    }

    public void AddEvent(SessionEventViewModel ev)
    {
        Events.Add(ev);
        EventAdded?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseInputRequested()
        => InputRequested?.Invoke(this, EventArgs.Empty);

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

    public void SetStatus(string line) => StatusLine = line;

    public Task<string> AskQuestion(string question)
    {
        var ev = new UserQuestionEventViewModel { Prompt = question };
        AddEvent(ev);
        RaiseInputRequested();
        return ev.Tcs.Task;
    }

    public Task<UserDecision> AskDecision(string message, IReadOnlyList<(UserDecision value, string label)> options)
    {
        var ev = new UserDecisionEventViewModel { Prompt = message };
        ev.SetOptions(options.Select((o, i) => (i, o.label, o.value)));
        AddEvent(ev);
        RaiseInputRequested();
        return ev.Tcs.Task;
    }

    public Task<ConfirmationResult> AskConfirmation(string title, string details)
    {
        var ev = new UserConfirmationEventViewModel { Title = title, Details = details };
        AddEvent(ev);
        RaiseInputRequested();
        return ev.Tcs.Task;
    }
}
