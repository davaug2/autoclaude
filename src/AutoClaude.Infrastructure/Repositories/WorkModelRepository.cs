using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using AutoClaude.Infrastructure.Data;
using Dapper;

namespace AutoClaude.Infrastructure.Repositories;

public class WorkModelRepository : IWorkModelRepository
{
    private readonly ConnectionFactory _connectionFactory;

    public WorkModelRepository(ConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<WorkModel?> GetByIdAsync(Guid id)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<WorkModel>(
            @"SELECT id AS Id, name AS Name, description AS Description,
                     is_builtin AS IsBuiltin, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM work_models WHERE id = @Id",
            new { Id = id });
    }

    public async Task<WorkModel?> GetByNameAsync(string name)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<WorkModel>(
            @"SELECT id AS Id, name AS Name, description AS Description,
                     is_builtin AS IsBuiltin, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM work_models WHERE name = @Name",
            new { Name = name });
    }

    public async Task<IReadOnlyList<WorkModel>> GetAllAsync()
    {
        using var conn = _connectionFactory.CreateConnection();
        var result = await conn.QueryAsync<WorkModel>(
            @"SELECT id AS Id, name AS Name, description AS Description,
                     is_builtin AS IsBuiltin, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM work_models ORDER BY name");
        return result.ToList();
    }

    public async Task InsertAsync(WorkModel model)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO work_models (id, name, description, is_builtin, created_at, updated_at)
              VALUES (@Id, @Name, @Description, @IsBuiltin, @CreatedAt, @UpdatedAt)",
            model);
    }
}
