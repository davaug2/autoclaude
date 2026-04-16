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
}
