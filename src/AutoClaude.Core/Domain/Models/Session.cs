using AutoClaude.Core.Domain.Enums;

namespace AutoClaude.Core.Domain.Models;

public class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkModelId { get; set; }
    public string? Name { get; set; }
    public string? Objective { get; set; }
    public string? TargetPath { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Created;
    public int CurrentPhaseOrdinal { get; set; }
    public string ContextJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public void UpdateStatus(SessionStatus newStatus)
    {
        Status = newStatus;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AdvancePhase(int ordinal)
    {
        CurrentPhaseOrdinal = ordinal;
        UpdatedAt = DateTime.UtcNow;
    }
}
