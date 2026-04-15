using AutoClaude.Infrastructure.Data;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace AutoClaude.Infrastructure.Tests.Data;

public class SqliteDatabaseInitializerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ConnectionFactory _factory;

    public SqliteDatabaseInitializerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _factory = new ConnectionFactory(_connection);
    }

    [Fact]
    public async Task InitializeAsync_ShouldCreateAllTables()
    {
        var initializer = new SqliteDatabaseInitializer(_factory);

        await initializer.InitializeAsync();

        var tables = (await _connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name"))
            .ToList();

        tables.Should().Contain("work_models");
        tables.Should().Contain("phases");
        tables.Should().Contain("sessions");
        tables.Should().Contain("tasks");
        tables.Should().Contain("subtasks");
        tables.Should().Contain("execution_history");
        tables.Should().HaveCount(6);
    }

    [Fact]
    public async Task InitializeAsync_ShouldBeIdempotent()
    {
        var initializer = new SqliteDatabaseInitializer(_factory);

        await initializer.InitializeAsync();
        var act = () => initializer.InitializeAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InitializeAsync_WorkModelsTable_ShouldHaveExpectedColumns()
    {
        var initializer = new SqliteDatabaseInitializer(_factory);
        await initializer.InitializeAsync();

        var columns = (await _connection.QueryAsync<dynamic>("PRAGMA table_info(work_models)"))
            .Select(c => (string)c.name)
            .ToList();

        columns.Should().Contain(new[] { "id", "name", "description", "is_builtin", "created_at", "updated_at" });
    }

    [Fact]
    public async Task InitializeAsync_SessionsTable_ShouldHaveExpectedColumns()
    {
        var initializer = new SqliteDatabaseInitializer(_factory);
        await initializer.InitializeAsync();

        var columns = (await _connection.QueryAsync<dynamic>("PRAGMA table_info(sessions)"))
            .Select(c => (string)c.name)
            .ToList();

        columns.Should().Contain(new[] { "id", "work_model_id", "name", "objective", "target_path",
            "status", "current_phase_ordinal", "context_json", "created_at", "updated_at" });
    }

    [Fact]
    public async Task InitializeAsync_ExecutionHistoryTable_ShouldHaveExpectedColumns()
    {
        var initializer = new SqliteDatabaseInitializer(_factory);
        await initializer.InitializeAsync();

        var columns = (await _connection.QueryAsync<dynamic>("PRAGMA table_info(execution_history)"))
            .Select(c => (string)c.name)
            .ToList();

        columns.Should().Contain(new[] { "id", "session_id", "task_id", "subtask_id", "phase_id",
            "cli_type", "prompt_sent", "system_prompt", "response_text", "response_json",
            "exit_code", "outcome", "started_at", "completed_at", "duration_ms", "error_message" });
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
