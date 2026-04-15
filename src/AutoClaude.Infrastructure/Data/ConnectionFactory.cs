using System.Data;
using Microsoft.Data.Sqlite;

namespace AutoClaude.Infrastructure.Data;

public class ConnectionFactory
{
    private readonly string? _connectionString;
    private readonly SqliteConnection? _sharedConnection;

    public ConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public ConnectionFactory(SqliteConnection sharedConnection)
    {
        _sharedConnection = sharedConnection;
    }

    public IDbConnection CreateConnection()
    {
        if (_sharedConnection != null)
            return new NonClosingConnection(_sharedConnection);

        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public bool IsShared => _sharedConnection != null;
}

internal class NonClosingConnection : IDbConnection
{
    private readonly IDbConnection _inner;

    public NonClosingConnection(IDbConnection inner) => _inner = inner;

    public string ConnectionString
    {
        get => _inner.ConnectionString!;
#pragma warning disable CS8767
        set => _inner.ConnectionString = value;
#pragma warning restore CS8767
    }

    public int ConnectionTimeout => _inner.ConnectionTimeout;
    public string Database => _inner.Database;
    public ConnectionState State => _inner.State;

    public IDbTransaction BeginTransaction() => _inner.BeginTransaction();
    public IDbTransaction BeginTransaction(IsolationLevel il) => _inner.BeginTransaction(il);
    public void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
    public void Close() { /* não fecha a conexão compartilhada */ }
    public IDbCommand CreateCommand() => _inner.CreateCommand();
    public void Open() { if (_inner.State != ConnectionState.Open) _inner.Open(); }
    public void Dispose() { /* não disposa a conexão compartilhada */ }
}
