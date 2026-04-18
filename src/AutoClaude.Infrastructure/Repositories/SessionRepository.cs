using AutoClaude.Core.Domain;
using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using AutoClaude.Infrastructure.Data;
using Dapper;

namespace AutoClaude.Infrastructure.Repositories;

public class SessionRepository : ISessionRepository
{
    private readonly ConnectionFactory _connectionFactory;

    public SessionRepository(ConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Session?> GetByIdAsync(Guid id)
    {
        using var conn = _connectionFactory.CreateConnection();
        var session = await conn.QuerySingleOrDefaultAsync<Session>(
            @"SELECT id AS Id, work_model_id AS WorkModelId, name AS Name,
                     objective AS Objective, target_path AS TargetPath,
                     status AS Status, current_phase_ordinal AS CurrentPhaseOrdinal,
                     context_json AS ContextJson, cli_session_id AS CliSessionId,
                     created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM sessions WHERE id = @Id",
            new { Id = id });
        if (session != null)
            SessionContextJson.HydrateAllowedDirectories(session);
        return session;
    }

    public async Task<IReadOnlyList<Session>> GetAllAsync()
    {
        using var conn = _connectionFactory.CreateConnection();
        var result = await conn.QueryAsync<Session>(
            @"SELECT id AS Id, work_model_id AS WorkModelId, name AS Name,
                     objective AS Objective, target_path AS TargetPath,
                     status AS Status, current_phase_ordinal AS CurrentPhaseOrdinal,
                     context_json AS ContextJson, cli_session_id AS CliSessionId,
                     created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM sessions ORDER BY created_at DESC");
        var list = result.ToList();
        foreach (var session in list)
            SessionContextJson.HydrateAllowedDirectories(session);
        return list;
    }

    public async Task InsertAsync(Session session)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO sessions (id, work_model_id, name, objective, target_path, status, current_phase_ordinal, context_json, created_at, updated_at)
              VALUES (@Id, @WorkModelId, @Name, @Objective, @TargetPath, @Status, @CurrentPhaseOrdinal, @ContextJson, @CreatedAt, @UpdatedAt)",
            new
            {
                session.Id, session.WorkModelId, session.Name, session.Objective, session.TargetPath,
                Status = session.Status.ToString(),
                session.CurrentPhaseOrdinal, session.ContextJson, session.CreatedAt, session.UpdatedAt
            });
    }

    public async Task UpdateStatusAsync(Guid id, SessionStatus status)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE sessions SET status = @Status, updated_at = @UpdatedAt WHERE id = @Id",
            new { Id = id, Status = status.ToString(), UpdatedAt = DateTime.UtcNow });
    }

    public async Task UpdateContextAsync(Guid id, string contextJson)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE sessions SET context_json = @ContextJson, updated_at = @UpdatedAt WHERE id = @Id",
            new { Id = id, ContextJson = contextJson, UpdatedAt = DateTime.UtcNow });
    }

    public async Task UpdateCliSessionIdAsync(Guid id, string? cliSessionId)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE sessions SET cli_session_id = @CliSessionId, updated_at = @UpdatedAt WHERE id = @Id",
            new { Id = id, CliSessionId = cliSessionId, UpdatedAt = DateTime.UtcNow });
    }

    public async Task UpdateTargetPathAsync(Guid id, string targetPath)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE sessions SET target_path = @TargetPath, updated_at = @UpdatedAt WHERE id = @Id",
            new { Id = id, TargetPath = targetPath, UpdatedAt = DateTime.UtcNow });
    }

    public async Task UpdateObjectiveAsync(Guid id, string objective)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE sessions SET objective = @Objective, updated_at = @UpdatedAt WHERE id = @Id",
            new { Id = id, Objective = objective, UpdatedAt = DateTime.UtcNow });
    }

    public async Task UpdateCurrentPhaseOrdinalAsync(Guid id, int ordinal)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE sessions SET current_phase_ordinal = @Ordinal, updated_at = @UpdatedAt WHERE id = @Id",
            new { Id = id, Ordinal = ordinal, UpdatedAt = DateTime.UtcNow });
    }

    public async Task DeleteAsync(Guid id)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM sessions WHERE id = @Id", new { Id = id });
    }
}
