using AutoClaude.Core.Domain.Enums;

namespace AutoClaude.Core.PhaseHandlers;

public class PhaseHandlerFactory
{
    private readonly IReadOnlyDictionary<PhaseType, IPhaseHandler> _handlers;

    public PhaseHandlerFactory(IEnumerable<IPhaseHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.HandledPhase);
    }

    public IPhaseHandler GetHandler(PhaseType phaseType)
    {
        if (!_handlers.TryGetValue(phaseType, out var handler))
            throw new InvalidOperationException($"No handler registered for phase type: {phaseType}");
        return handler;
    }
}
