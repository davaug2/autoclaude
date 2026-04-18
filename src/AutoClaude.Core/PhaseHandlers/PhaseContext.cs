using AutoClaude.Core.Domain.Models;

namespace AutoClaude.Core.PhaseHandlers;

public class PhaseContext
{
    public required Session Session { get; init; }
    public required Phase Phase { get; init; }
    public TaskItem? CurrentTask { get; init; }
    public SubtaskItem? CurrentSubtask { get; init; }
    public string? UserInstruction { get; init; }
    public SessionMemory Memory { get; init; } = new();
    public Func<Task> SaveMemoryAsync { get; init; } = () => Task.CompletedTask;
    public bool AllowWrite { get; init; }

    /// <summary>
    /// Maps WorkingDirectory → Claude CLI session ID.
    /// Allows resuming the correct session when switching between directories.
    /// </summary>
    public Dictionary<string, string> CliSessionMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Convenience: get/set CLI session ID for the session's default TargetPath.
    /// Used by phase handlers that always work in the main directory.
    /// </summary>
    public string? CliSessionId
    {
        get => GetCliSessionId(Session.TargetPath);
        set
        {
            if (Session.TargetPath != null && value != null)
                CliSessionMap[Session.TargetPath] = value;
        }
    }

    public string? GetCliSessionId(string? workDir)
    {
        if (workDir != null && CliSessionMap.TryGetValue(workDir, out var id))
            return id;
        return null;
    }

    public void SetCliSessionId(string workDir, string sessionId)
    {
        CliSessionMap[workDir] = sessionId;
    }
}
