using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using AutoClaude.Infrastructure.Data;
using Dapper;

namespace AutoClaude.Infrastructure.Repositories;

public class ExecutionRecordRepository : IExecutionRecordRepository
{
    private readonly ConnectionFactory _connectionFactory;

    public ExecutionRecordRepository(ConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ExecutionRecord>> GetBySessionIdAsync(Guid sessionId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var result = await conn.QueryAsync<ExecutionRecord>(
            @"SELECT id AS Id, session_id AS SessionId, task_id AS TaskId,
                     subtask_id AS SubtaskId, phase_id AS PhaseId, cli_type AS CliType,
                     prompt_sent AS PromptSent, system_prompt AS SystemPrompt,
                     response_text AS ResponseText, response_json AS ResponseJson,
                     exit_code AS ExitCode, outcome AS Outcome,
                     started_at AS StartedAt, completed_at AS CompletedAt,
                     duration_ms AS DurationMs, error_message AS ErrorMessage
              FROM execution_history WHERE session_id = @SessionId ORDER BY started_at",
            new { SessionId = sessionId });
        return result.ToList();
    }

    public async Task<IReadOnlyList<ExecutionRecord>> GetBySubtaskIdAsync(Guid subtaskId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var result = await conn.QueryAsync<ExecutionRecord>(
            @"SELECT id AS Id, session_id AS SessionId, task_id AS TaskId,
                     subtask_id AS SubtaskId, phase_id AS PhaseId, cli_type AS CliType,
                     prompt_sent AS PromptSent, system_prompt AS SystemPrompt,
                     response_text AS ResponseText, response_json AS ResponseJson,
                     exit_code AS ExitCode, outcome AS Outcome,
                     started_at AS StartedAt, completed_at AS CompletedAt,
                     duration_ms AS DurationMs, error_message AS ErrorMessage
              FROM execution_history WHERE subtask_id = @SubtaskId ORDER BY started_at",
            new { SubtaskId = subtaskId });
        return result.ToList();
    }

    public async Task InsertAsync(ExecutionRecord record)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO execution_history (id, session_id, task_id, subtask_id, phase_id, cli_type,
                     prompt_sent, system_prompt, response_text, response_json,
                     exit_code, outcome, started_at, completed_at, duration_ms, error_message)
              VALUES (@Id, @SessionId, @TaskId, @SubtaskId, @PhaseId, @CliType,
                     @PromptSent, @SystemPrompt, @ResponseText, @ResponseJson,
                     @ExitCode, @Outcome, @StartedAt, @CompletedAt, @DurationMs, @ErrorMessage)",
            new
            {
                record.Id, record.SessionId, record.TaskId, record.SubtaskId, record.PhaseId,
                record.CliType, record.PromptSent, record.SystemPrompt,
                record.ResponseText, record.ResponseJson,
                record.ExitCode, Outcome = record.Outcome.ToString(),
                record.StartedAt, record.CompletedAt, record.DurationMs, record.ErrorMessage
            });
    }

    public async Task UpdateAsync(ExecutionRecord record)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            @"UPDATE execution_history SET
                     response_text = @ResponseText, response_json = @ResponseJson,
                     exit_code = @ExitCode, outcome = @Outcome,
                     completed_at = @CompletedAt, duration_ms = @DurationMs, error_message = @ErrorMessage
              WHERE id = @Id",
            new
            {
                record.Id, record.ResponseText, record.ResponseJson,
                record.ExitCode, Outcome = record.Outcome.ToString(),
                record.CompletedAt, record.DurationMs, record.ErrorMessage
            });
    }
}
