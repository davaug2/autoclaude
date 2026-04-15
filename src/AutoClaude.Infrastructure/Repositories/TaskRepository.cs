using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using AutoClaude.Infrastructure.Data;
using Dapper;

namespace AutoClaude.Infrastructure.Repositories;

public class TaskRepository : ITaskRepository
{
    private readonly ConnectionFactory _connectionFactory;

    public TaskRepository(ConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<TaskItem?> GetByIdAsync(Guid id)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<TaskItem>(
            @"SELECT id AS Id, session_id AS SessionId, title AS Title,
                     description AS Description, ordinal AS Ordinal,
                     status AS Status, result_summary AS ResultSummary,
                     created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM tasks WHERE id = @Id",
            new { Id = id });
    }

    public async Task<IReadOnlyList<TaskItem>> GetBySessionIdAsync(Guid sessionId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var result = await conn.QueryAsync<TaskItem>(
            @"SELECT id AS Id, session_id AS SessionId, title AS Title,
                     description AS Description, ordinal AS Ordinal,
                     status AS Status, result_summary AS ResultSummary,
                     created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM tasks WHERE session_id = @SessionId ORDER BY ordinal",
            new { SessionId = sessionId });
        return result.ToList();
    }

    public async Task InsertAsync(TaskItem task)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO tasks (id, session_id, title, description, ordinal, status, result_summary, created_at, updated_at)
              VALUES (@Id, @SessionId, @Title, @Description, @Ordinal, @Status, @ResultSummary, @CreatedAt, @UpdatedAt)",
            new
            {
                task.Id, task.SessionId, task.Title, task.Description, task.Ordinal,
                Status = task.Status.ToString(),
                task.ResultSummary, task.CreatedAt, task.UpdatedAt
            });
    }

    public async Task UpdateStatusAsync(Guid id, TaskItemStatus status)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE tasks SET status = @Status, updated_at = @UpdatedAt WHERE id = @Id",
            new { Id = id, Status = status.ToString(), UpdatedAt = DateTime.UtcNow });
    }

    public async Task UpdateResultSummaryAsync(Guid id, string resultSummary)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE tasks SET result_summary = @ResultSummary, updated_at = @UpdatedAt WHERE id = @Id",
            new { Id = id, ResultSummary = resultSummary, UpdatedAt = DateTime.UtcNow });
    }
}
