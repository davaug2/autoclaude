using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;

namespace AutoClaude.Core.Ports;

public interface ITaskRepository
{
    Task<TaskItem?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<TaskItem>> GetBySessionIdAsync(Guid sessionId);
    Task InsertAsync(TaskItem task);
    Task UpdateStatusAsync(Guid id, TaskItemStatus status);
    Task UpdateResultSummaryAsync(Guid id, string resultSummary);
}
