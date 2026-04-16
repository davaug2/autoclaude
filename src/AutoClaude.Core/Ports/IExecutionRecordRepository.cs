using AutoClaude.Core.Domain.Models;

namespace AutoClaude.Core.Ports;

public interface IExecutionRecordRepository
{
    Task<IReadOnlyList<ExecutionRecord>> GetBySessionIdAsync(Guid sessionId);
    Task<IReadOnlyList<ExecutionRecord>> GetBySubtaskIdAsync(Guid subtaskId);
    Task InsertAsync(ExecutionRecord record);
    Task UpdateAsync(ExecutionRecord record);
    Task DeleteBySessionIdAsync(Guid sessionId);
}
