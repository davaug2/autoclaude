using AutoClaude.Core.Ports;
using Dapper;

namespace AutoClaude.Infrastructure.Data;

public class SqliteDatabaseInitializer : IDatabaseInitializer
{
    private readonly ConnectionFactory _connectionFactory;

    public SqliteDatabaseInitializer(ConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InitializeAsync()
    {
        DapperTypeHandlerRegistration.Register();

        using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(CreateTablesSql);
    }

    private const string CreateTablesSql = @"
        CREATE TABLE IF NOT EXISTS work_models (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL UNIQUE,
            description TEXT,
            is_builtin INTEGER DEFAULT 0,
            created_at TEXT DEFAULT (datetime('now')),
            updated_at TEXT DEFAULT (datetime('now'))
        );

        CREATE TABLE IF NOT EXISTS phases (
            id TEXT PRIMARY KEY,
            work_model_id TEXT REFERENCES work_models(id),
            name TEXT NOT NULL,
            phase_type TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            description TEXT,
            prompt_template TEXT,
            system_prompt TEXT,
            repeat_mode TEXT DEFAULT 'Once',
            UNIQUE(work_model_id, ordinal)
        );

        CREATE TABLE IF NOT EXISTS sessions (
            id TEXT PRIMARY KEY,
            work_model_id TEXT REFERENCES work_models(id),
            name TEXT,
            objective TEXT,
            target_path TEXT,
            status TEXT DEFAULT 'Created',
            current_phase_ordinal INTEGER DEFAULT 0,
            context_json TEXT DEFAULT '{}',
            created_at TEXT DEFAULT (datetime('now')),
            updated_at TEXT DEFAULT (datetime('now'))
        );

        CREATE TABLE IF NOT EXISTS tasks (
            id TEXT PRIMARY KEY,
            session_id TEXT REFERENCES sessions(id),
            title TEXT NOT NULL,
            description TEXT,
            ordinal INTEGER NOT NULL,
            status TEXT DEFAULT 'Pending',
            result_summary TEXT,
            created_at TEXT DEFAULT (datetime('now')),
            updated_at TEXT DEFAULT (datetime('now'))
        );

        CREATE TABLE IF NOT EXISTS subtasks (
            id TEXT PRIMARY KEY,
            task_id TEXT REFERENCES tasks(id),
            session_id TEXT REFERENCES sessions(id),
            title TEXT NOT NULL,
            prompt TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            status TEXT DEFAULT 'Pending',
            result_summary TEXT,
            validation_note TEXT,
            created_at TEXT DEFAULT (datetime('now')),
            updated_at TEXT DEFAULT (datetime('now'))
        );

        CREATE TABLE IF NOT EXISTS execution_history (
            id TEXT PRIMARY KEY,
            session_id TEXT REFERENCES sessions(id),
            task_id TEXT,
            subtask_id TEXT,
            phase_id TEXT REFERENCES phases(id),
            cli_type TEXT DEFAULT 'claude',
            prompt_sent TEXT NOT NULL,
            system_prompt TEXT,
            response_text TEXT,
            response_json TEXT,
            exit_code INTEGER,
            outcome TEXT DEFAULT 'Pending',
            started_at TEXT,
            completed_at TEXT,
            duration_ms INTEGER,
            error_message TEXT
        );
    ";
}
