using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;

namespace AutoClaude.Core.Ports;

public interface ISubtaskRepository
{
    Task<SubtaskItem?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<SubtaskItem>> GetByTaskIdAsync(Guid taskId);
    Task<IReadOnlyList<SubtaskItem>> GetBySessionIdAsync(Guid sessionId);
    Task InsertAsync(SubtaskItem subtask);
    Task UpdateStatusAsync(Guid id, SubtaskItemStatus status);
    Task UpdateResultSummaryAsync(Guid id, string resultSummary);
    Task UpdateValidationNoteAsync(Guid id, string validationNote);
    Task DeleteBySessionIdAsync(Guid sessionId);
}
