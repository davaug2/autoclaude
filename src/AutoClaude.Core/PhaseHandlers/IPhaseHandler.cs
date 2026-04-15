using AutoClaude.Core.Domain.Enums;

namespace AutoClaude.Core.PhaseHandlers;

public interface IPhaseHandler
{
    PhaseType HandledPhase { get; }
    Task<PhaseResult> HandleAsync(PhaseContext context, CancellationToken ct = default);
}
