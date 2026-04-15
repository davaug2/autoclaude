using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using AutoClaude.Infrastructure.Data;
using Dapper;

namespace AutoClaude.Infrastructure.Repositories;

public class PhaseRepository : IPhaseRepository
{
    private readonly ConnectionFactory _connectionFactory;

    public PhaseRepository(ConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Phase?> GetByIdAsync(Guid id)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Phase>(
            @"SELECT id AS Id, work_model_id AS WorkModelId, name AS Name,
                     phase_type AS PhaseType, ordinal AS Ordinal, description AS Description,
                     prompt_template AS PromptTemplate, system_prompt AS SystemPrompt,
                     repeat_mode AS RepeatMode
              FROM phases WHERE id = @Id",
            new { Id = id });
    }

    public async Task<IReadOnlyList<Phase>> GetByWorkModelIdAsync(Guid workModelId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var result = await conn.QueryAsync<Phase>(
            @"SELECT id AS Id, work_model_id AS WorkModelId, name AS Name,
                     phase_type AS PhaseType, ordinal AS Ordinal, description AS Description,
                     prompt_template AS PromptTemplate, system_prompt AS SystemPrompt,
                     repeat_mode AS RepeatMode
              FROM phases WHERE work_model_id = @WorkModelId ORDER BY ordinal",
            new { WorkModelId = workModelId });
        return result.ToList();
    }

    public async Task InsertAsync(Phase phase)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO phases (id, work_model_id, name, phase_type, ordinal, description, prompt_template, system_prompt, repeat_mode)
              VALUES (@Id, @WorkModelId, @Name, @PhaseType, @Ordinal, @Description, @PromptTemplate, @SystemPrompt, @RepeatMode)",
            new
            {
                phase.Id, phase.WorkModelId, phase.Name,
                PhaseType = phase.PhaseType.ToString(),
                phase.Ordinal, phase.Description, phase.PromptTemplate, phase.SystemPrompt,
                RepeatMode = phase.RepeatMode.ToString()
            });
    }
}
