using AutoClaude.Core.Domain.Enums;

namespace AutoClaude.Core.Domain.Models;

public class SubtaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid SessionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public SubtaskItemStatus Status { get; set; } = SubtaskItemStatus.Pending;
    public string? ResultSummary { get; set; }
    public string? ValidationNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public void MarkRunning()
    {
        Status = SubtaskItemStatus.Running;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkCompleted(string resultSummary)
    {
        Status = SubtaskItemStatus.Completed;
        ResultSummary = resultSummary;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Status = SubtaskItemStatus.Failed;
        ResultSummary = error;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkSkipped()
    {
        Status = SubtaskItemStatus.Skipped;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetValidation(string note)
    {
        ValidationNote = note;
        UpdatedAt = DateTime.UtcNow;
    }
}
