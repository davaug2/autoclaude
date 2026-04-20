using System.Collections.ObjectModel;
using System.Text.Json;
using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using AutoClaude.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoClaude.App.ViewModels;

public sealed partial class SessionDetailsViewModel : ObservableObject
{
    private readonly SessionService _sessionService;
    private readonly IExecutionRecordRepository _executionRepo;
    private Guid _sessionId;

    [ObservableProperty]
    private string _objective = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _targetPath = "";

    [ObservableProperty]
    private string _cliSessionId = "";

    [ObservableProperty]
    private string _analysisResult = "";

    [ObservableProperty]
    private string _newDirectoryPath = "";

    [ObservableProperty]
    private bool _isAddingDirectory;

    public ObservableCollection<string> AllowedDirectories { get; } = new();
    public ObservableCollection<MemoryEntryItem> PersistentMemories { get; } = new();
    public ObservableCollection<MemoryEntryItem> TemporaryMemories { get; } = new();
    public ObservableCollection<TaskTreeItem> Tasks { get; } = new();
    public ObservableCollection<ExecutionItem> Executions { get; } = new();

    public SessionDetailsViewModel(SessionService sessionService, IExecutionRecordRepository executionRepo)
    {
        _sessionService = sessionService;
        _executionRepo = executionRepo;
    }

    public async Task LoadAsync(Guid sessionId)
    {
        _sessionId = sessionId;

        var session = await _sessionService.GetAsync(sessionId);

        Objective = session.Objective ?? "";
        Status = session.Status.ToString();
        TargetPath = session.TargetPath ?? "(nao definido)";
        CliSessionId = session.CliSessionId ?? "";

        // Directories
        AllowedDirectories.Clear();
        foreach (var dir in session.AllowedDirectories)
            AllowedDirectories.Add(dir);

        // Analysis result
        AnalysisResult = ExtractAnalysisResult(session.ContextJson);

        // Memory
        LoadMemory(session.ContextJson);

        // Tasks & Subtasks
        var (tasks, subtasks) = await _sessionService.GetStatusAsync(sessionId);
        Tasks.Clear();
        foreach (var task in tasks.OrderBy(t => t.Ordinal))
        {
            var taskSubtasks = subtasks
                .Where(s => s.TaskId == task.Id)
                .OrderBy(s => s.Ordinal)
                .Select(s =>
                {
                    var title = s.Title;
                    if (!string.IsNullOrEmpty(s.WorkingDirectory))
                        title += $"  [{s.WorkingDirectory}]";
                    return new SubtaskTreeItem(
                        title, StatusIcon(s.Status), s.Status.ToString(), s.ResultSummary, s.WorkingDirectory);
                })
                .ToList();

            Tasks.Add(new TaskTreeItem(
                task.Title, StatusIcon(task.Status), task.Status.ToString(),
                task.ResultSummary, new ObservableCollection<SubtaskTreeItem>(taskSubtasks)));
        }

        // Executions
        var executions = await _executionRepo.GetBySessionIdAsync(sessionId);
        Executions.Clear();
        foreach (var exec in executions.OrderByDescending(e => e.StartedAt))
        {
            Executions.Add(new ExecutionItem(
                exec.Outcome.ToString(),
                exec.StartedAt?.ToString("HH:mm:ss") ?? "-",
                exec.CompletedAt?.ToString("HH:mm:ss") ?? "-",
                exec.PromptSent,
                exec.ResponseText,
                exec.ErrorMessage));
        }
    }

    private void LoadMemory(string contextJson)
    {
        PersistentMemories.Clear();
        TemporaryMemories.Clear();

        try
        {
            using var doc = JsonDocument.Parse(contextJson);
            if (!doc.RootElement.TryGetProperty("memory", out var memProp))
                return;

            var memory = JsonSerializer.Deserialize<SessionMemory>(memProp.GetRawText());
            if (memory == null) return;

            foreach (var e in memory.Persistent)
                PersistentMemories.Add(new MemoryEntryItem { Question = e.Question, Answer = e.Answer });
            foreach (var e in memory.Temporary)
                TemporaryMemories.Add(new MemoryEntryItem { Question = e.Question, Answer = e.Answer });
        }
        catch (JsonException) { }
    }

    // --- Objetivo ---

    [RelayCommand]
    private async Task SaveObjectiveAsync()
    {
        await _sessionService.UpdateObjectiveAsync(_sessionId, Objective);
    }

    [RelayCommand]
    private async Task SaveCliSessionIdAsync()
    {
        var value = string.IsNullOrWhiteSpace(CliSessionId) ? null : CliSessionId.Trim();
        await _sessionService.UpdateCliSessionIdAsync(_sessionId, value);
    }

    // --- Diretórios ---

    [RelayCommand]
    private void ShowAddDirectory() => IsAddingDirectory = true;

    [RelayCommand]
    private async Task ConfirmAddDirectoryAsync()
    {
        var path = NewDirectoryPath.Trim();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        if (!AllowedDirectories.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            AllowedDirectories.Add(path);
            await PersistDirectoriesAsync();
        }

        NewDirectoryPath = "";
        IsAddingDirectory = false;
    }

    [RelayCommand]
    private async Task RemoveDirectoryAsync(string? path)
    {
        if (path == null) return;
        AllowedDirectories.Remove(path);
        await PersistDirectoriesAsync();
    }

    private async Task PersistDirectoriesAsync()
    {
        var session = await _sessionService.GetAsync(_sessionId);
        session.AllowedDirectories = AllowedDirectories.ToList();
        await _sessionService.PersistAllowedDirectoriesAsync(session);
    }

    // --- Memórias ---

    [RelayCommand]
    private void AddPersistentMemory()
    {
        PersistentMemories.Add(new MemoryEntryItem { Question = "", Answer = "" });
    }

    [RelayCommand]
    private void AddTemporaryMemory()
    {
        TemporaryMemories.Add(new MemoryEntryItem { Question = "", Answer = "" });
    }

    [RelayCommand]
    private void RemovePersistentMemory(MemoryEntryItem? item)
    {
        if (item != null) PersistentMemories.Remove(item);
    }

    [RelayCommand]
    private void RemoveTemporaryMemory(MemoryEntryItem? item)
    {
        if (item != null) TemporaryMemories.Remove(item);
    }

    [RelayCommand]
    private async Task SaveMemoriesAsync()
    {
        var session = await _sessionService.GetAsync(_sessionId);

        var memory = new SessionMemory();
        foreach (var e in PersistentMemories.Where(m => !string.IsNullOrWhiteSpace(m.Question)))
            memory.AddPersistent(e.Question, e.Answer);
        foreach (var e in TemporaryMemories.Where(m => !string.IsNullOrWhiteSpace(m.Question)))
            memory.AddTemporary(e.Question, e.Answer);

        var contextDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(session.ContextJson)
            ?? new Dictionary<string, JsonElement>();
        contextDict["memory"] = JsonSerializer.SerializeToElement(memory);
        session.ContextJson = JsonSerializer.Serialize(contextDict);

        // Persist context (includes memory + directories)
        session.AllowedDirectories = AllowedDirectories.ToList();
        await _sessionService.PersistAllowedDirectoriesAsync(session);
    }

    // --- Helpers ---

    private static string ExtractAnalysisResult(string contextJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(contextJson);
            if (doc.RootElement.TryGetProperty("analysis_result", out var prop))
                return prop.GetString() ?? "";
        }
        catch (JsonException) { }
        return "";
    }

    private static string StatusIcon(TaskItemStatus status) => status switch
    {
        TaskItemStatus.Completed => "\u2705",
        TaskItemStatus.InProgress => "\uD83D\uDD04",
        TaskItemStatus.Failed => "\u274C",
        _ => "\u23F3"
    };

    private static string StatusIcon(SubtaskItemStatus status) => status switch
    {
        SubtaskItemStatus.Completed => "\u2705",
        SubtaskItemStatus.Running => "\uD83D\uDD04",
        SubtaskItemStatus.Failed => "\u274C",
        SubtaskItemStatus.Skipped => "\u23ED",
        _ => "\u23F3"
    };

    private static string? Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length > maxLength ? text[..(maxLength - 3)] + "..." : text;
    }
}

public sealed partial class MemoryEntryItem : ObservableObject
{
    [ObservableProperty]
    private string _question = "";

    [ObservableProperty]
    private string _answer = "";
}

public sealed class TaskTreeItem
{
    public string Title { get; }
    public string StatusIcon { get; }
    public string StatusText { get; }
    public string? ResultSummary { get; }
    public ObservableCollection<SubtaskTreeItem> Subtasks { get; }

    public TaskTreeItem(string title, string statusIcon, string statusText, string? resultSummary, ObservableCollection<SubtaskTreeItem> subtasks)
    {
        Title = title;
        StatusIcon = statusIcon;
        StatusText = statusText;
        ResultSummary = resultSummary;
        Subtasks = subtasks;
    }
}

public sealed class SubtaskTreeItem
{
    public string Title { get; }
    public string StatusIcon { get; }
    public string StatusText { get; }
    public string? ResultSummary { get; }
    public string? WorkingDirectory { get; }

    public SubtaskTreeItem(string title, string statusIcon, string statusText, string? resultSummary, string? workingDirectory = null)
    {
        Title = title;
        StatusIcon = statusIcon;
        StatusText = statusText;
        ResultSummary = resultSummary;
        WorkingDirectory = workingDirectory;
    }
}

public sealed class ExecutionItem
{
    public string Outcome { get; }
    public string StartedAt { get; }
    public string CompletedAt { get; }
    public string? Input { get; }
    public string? Output { get; }
    public string? ErrorMessage { get; }

    public ExecutionItem(string outcome, string startedAt, string completedAt, string? input, string? output, string? errorMessage)
    {
        Outcome = outcome;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Input = input;
        Output = output;
        ErrorMessage = errorMessage;
    }
}
