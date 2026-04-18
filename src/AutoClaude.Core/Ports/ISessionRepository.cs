using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;

namespace AutoClaude.Core.Ports;

public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<Session>> GetAllAsync();
    Task InsertAsync(Session session);
    Task UpdateStatusAsync(Guid id, SessionStatus status);
    Task UpdateContextAsync(Guid id, string contextJson);
    Task UpdateObjectiveAsync(Guid id, string objective);
    Task UpdateTargetPathAsync(Guid id, string targetPath);
    Task UpdateCliSessionIdAsync(Guid id, string? cliSessionId);
    Task UpdateCurrentPhaseOrdinalAsync(Guid id, int ordinal);
    Task DeleteAsync(Guid id);
}
