using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using AutoClaude.Infrastructure.Data;
using Dapper;

namespace AutoClaude.Infrastructure.Repositories;

public class SubtaskRepository : ISubtaskRepository
{
    private readonly ConnectionFactory _connectionFactory;

    public SubtaskRepository(ConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SubtaskItem?> GetByIdAsync(Guid id)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<SubtaskItem>(
            @"SELECT id AS Id, task_id AS TaskId, session_id AS SessionId,
                     title AS Title, prompt AS Prompt, ordinal AS Ordinal,
                     status AS Status, result_summary AS ResultSummary,
                     validation_note AS ValidationNote,
                     created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM subtasks WHERE id = @Id",
            new { Id = id });
    }

    public async Task<IReadOnlyList<SubtaskItem>> GetByTaskIdAsync(Guid taskId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var result = await conn.QueryAsync<SubtaskItem>(
            @"SELECT id AS Id, task_id AS TaskId, session_id AS SessionId,
                     title AS Title, prompt AS Prompt, ordinal AS Ordinal,
                     status AS Status, result_summary AS ResultSummary,
                     validation_note AS ValidationNote,
                     created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM subtasks WHERE task_id = @TaskId ORDER BY ordinal",
            new { TaskId = taskId });
        return result.ToList();
    }

    public async Task<IReadOnlyList<SubtaskItem>> GetBySessionIdAsync(Guid sessionId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var result = await conn.QueryAsync<SubtaskItem>(
            @"SELECT id AS Id, task_id AS TaskId, session_id AS SessionId,
                     title AS Title, prompt AS Prompt, ordinal AS Ordinal,
                     status AS Status, result_summary AS ResultSummary,
                     validation_note AS ValidationNote,
                     created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM subtasks WHERE session_id = @SessionId ORDER BY ordinal",
            new { SessionId = sessionId });
        return result.ToList();
    }

    public async Task InsertAsync(SubtaskItem subtask)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO subtasks (id, task_id, session_id, title, prompt, ordinal, status, result_summary, validation_note, created_at, updated_at)
              VALUES (@Id, @TaskId, @SessionId, @Title, @Prompt, @Ordinal, @Status, @ResultSummary, @ValidationNote, @CreatedAt, @UpdatedAt)",
            new
            {
                subtask.Id, subtask.TaskId, subtask.SessionId, subtask.Title, subtask.Prompt,
                subtask.Ordinal, Status = subtask.Status.ToString(),
                subtask.ResultSummary, subtask.ValidationNote, subtask.CreatedAt, subtask.UpdatedAt
            });
    }

    public async Task UpdateStatusAsync(Guid id, SubtaskItemStatus status)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE subtasks SET status = @Status, updated_at = @UpdatedAt WHERE id = @Id",
            new { Id = id, Status = status.ToString(), UpdatedAt = DateTime.UtcNow });
    }

    public async Task UpdateResultSummaryAsync(Guid id, string resultSummary)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE subtasks SET result_summary = @ResultSummary, updated_at = @UpdatedAt WHERE id = @Id",
            new { Id = id, ResultSummary = resultSummary, UpdatedAt = DateTime.UtcNow });
    }

    public async Task UpdateValidationNoteAsync(Guid id, string validationNote)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE subtasks SET validation_note = @ValidationNote, updated_at = @UpdatedAt WHERE id = @Id",
            new { Id = id, ValidationNote = validationNote, UpdatedAt = DateTime.UtcNow });
    }

    public async Task DeleteBySessionIdAsync(Guid sessionId)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM subtasks WHERE session_id = @SessionId", new { SessionId = sessionId });
    }
}
