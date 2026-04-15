using AutoClaude.Core.Domain.Enums;

namespace AutoClaude.Core.Domain.Events;

public record PhaseStartedEvent(Guid SessionId, Guid PhaseId, PhaseType PhaseType, DateTime OccurredAt);

public record PhaseCompletedEvent(Guid SessionId, Guid PhaseId, PhaseType PhaseType, bool Success, string? ErrorMessage, DateTime OccurredAt);

public record TaskCreatedEvent(Guid SessionId, Guid TaskId, string Title, int Ordinal, DateTime OccurredAt);

public record SubtaskStartedEvent(Guid SessionId, Guid TaskId, Guid SubtaskId, DateTime OccurredAt);

public record SubtaskCompletedEvent(Guid SessionId, Guid TaskId, Guid SubtaskId, bool Success, DateTime OccurredAt);

public record SessionCompletedEvent(Guid SessionId, bool Success, DateTime OccurredAt);
