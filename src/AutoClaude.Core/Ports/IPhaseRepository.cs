using AutoClaude.Core.Domain.Models;

namespace AutoClaude.Core.Ports;

public interface IPhaseRepository
{
    Task<Phase?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<Phase>> GetByWorkModelIdAsync(Guid workModelId);
    Task InsertAsync(Phase phase);
}
