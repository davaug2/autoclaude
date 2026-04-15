using AutoClaude.Core.Domain.Enums;

namespace AutoClaude.Core.Domain.Models;

public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Ordinal { get; set; }
    public TaskItemStatus Status { get; set; } = TaskItemStatus.Pending;
    public string? ResultSummary { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public void MarkInProgress()
    {
        Status = TaskItemStatus.InProgress;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkCompleted(string? resultSummary = null)
    {
        Status = TaskItemStatus.Completed;
        ResultSummary = resultSummary;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string? resultSummary = null)
    {
        Status = TaskItemStatus.Failed;
        ResultSummary = resultSummary;
        UpdatedAt = DateTime.UtcNow;
    }
}
