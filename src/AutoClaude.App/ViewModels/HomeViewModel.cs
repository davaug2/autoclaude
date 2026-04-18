using System.Collections.ObjectModel;
using System.Collections.Specialized;
using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoClaude.App.ViewModels;

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly SessionService _sessionService;

    [ObservableProperty]
    private string _objective = "";

    [ObservableProperty]
    private string _targetPath = "";

    [ObservableProperty]
    private WorkModelViewItem? _selectedWorkModel;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<SessionListItem> Sessions { get; } = new();
    public ObservableCollection<WorkModelViewItem> WorkModels { get; } = new();

    [ObservableProperty]
    private bool _hasNoSessions = true;

    public event EventHandler<SessionOpenRequestedEventArgs>? SessionOpenRequested;

    public HomeViewModel(SessionService sessionService)
    {
        _sessionService = sessionService;
        Sessions.CollectionChanged += (_, _) => HasNoSessions = Sessions.Count == 0;
    }

    public async Task InitializeAsync()
    {
        var models = await _sessionService.ListWorkModelsAsync();
        WorkModels.Clear();
        foreach (var m in models.OrderBy(x => x.Name))
            WorkModels.Add(new WorkModelViewItem(m.Id, m.Name, m.Description));

        SelectedWorkModel = WorkModels.FirstOrDefault(w =>
            w.Name.Equals("CascadeFlow", StringComparison.OrdinalIgnoreCase))
            ?? WorkModels.FirstOrDefault();

        await RefreshSessionsAsync();
    }

    [RelayCommand]
    public async Task RefreshSessionsAsync()
    {
        var list = await _sessionService.ListWithPhaseAsync();
        Sessions.Clear();
        foreach (var (s, phaseName) in list.OrderByDescending(x => x.session.UpdatedAt))
            Sessions.Add(SessionListItem.FromSession(s, phaseName));
    }

    private bool CanNewSession() => !IsBusy && !string.IsNullOrWhiteSpace(Objective);

    [RelayCommand(CanExecute = nameof(CanNewSession))]
    private async Task NewSessionAsync()
    {
        var path = string.IsNullOrWhiteSpace(TargetPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : TargetPath.Trim();

        try
        {
            IsBusy = true;
            var session = await _sessionService.CreateAsync(
                Objective.Trim(), targetPath: path, workModelId: SelectedWorkModel?.Id);

            var name = string.IsNullOrEmpty(session.Name)
                ? session.Id.ToString()[..8] + "..."
                : session.Name;

            SessionOpenRequested?.Invoke(this, new SessionOpenRequestedEventArgs(
                session.Id, name, SessionOpenMode.RunNew));

            Objective = "";
            await RefreshSessionsAsync();
        }
        catch (Exception ex)
        {
            // Will be shown in session tab log if open
            System.Diagnostics.Debug.WriteLine($"NewSession error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenSession(SessionListItem? item)
    {
        if (item == null) return;

        var mode = item.Status is SessionStatus.Paused or SessionStatus.Created or SessionStatus.Running
            ? SessionOpenMode.Resume
            : SessionOpenMode.ViewOnly;

        SessionOpenRequested?.Invoke(this, new SessionOpenRequestedEventArgs(
            item.Id, item.Name, mode));
    }

    [RelayCommand]
    private async Task DeleteSessionAsync(SessionListItem? item)
    {
        if (item == null) return;
        await _sessionService.DeleteAsync(item.Id);
        await RefreshSessionsAsync();
    }

    partial void OnIsBusyChanged(bool value) => NewSessionCommand.NotifyCanExecuteChanged();
    partial void OnObjectiveChanged(string value) => NewSessionCommand.NotifyCanExecuteChanged();
}

public sealed class SessionListItem
{
    public Guid Id { get; }
    public string Name { get; }
    public SessionStatus Status { get; }
    public string StatusText { get; }
    public string ObjectivePreview { get; }
    public string? PhaseName { get; }

    private SessionListItem(Guid id, string name, SessionStatus status, string objectivePreview, string? phaseName)
    {
        Id = id;
        Name = name;
        Status = status;
        PhaseName = phaseName;
        StatusText = phaseName != null ? $"{status} — {phaseName}" : status.ToString();
        ObjectivePreview = objectivePreview;
    }

    public static SessionListItem FromSession(Session s, string? phaseName = null)
    {
        var obj = s.Objective ?? "";
        var preview = obj.Length > 100 ? obj[..97] + "..." : obj;
        var name = string.IsNullOrEmpty(s.Name) ? s.Id.ToString()[..8] + "..." : s.Name;
        return new SessionListItem(s.Id, name, s.Status, preview, phaseName);
    }
}

public sealed class WorkModelViewItem
{
    public Guid Id { get; }
    public string Name { get; }
    public string? Description { get; }

    public WorkModelViewItem(Guid id, string name, string? description)
    {
        Id = id;
        Name = name;
        Description = description;
    }

    public override string ToString() => Description is { Length: > 0 } d ? $"{Name} — {d}" : Name;
}

public sealed class SessionOpenRequestedEventArgs : EventArgs
{
    public Guid SessionId { get; }
    public string SessionName { get; }
    public SessionOpenMode Mode { get; }

    public SessionOpenRequestedEventArgs(Guid sessionId, string sessionName, SessionOpenMode mode)
    {
        SessionId = sessionId;
        SessionName = sessionName;
        Mode = mode;
    }
}

public enum SessionOpenMode
{
    RunNew,
    Resume,
    ViewOnly
}
