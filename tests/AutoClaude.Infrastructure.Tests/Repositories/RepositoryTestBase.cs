using AutoClaude.Infrastructure.Data;
using Microsoft.Data.Sqlite;

namespace AutoClaude.Infrastructure.Tests.Repositories;

public abstract class RepositoryTestBase : IAsyncLifetime
{
    protected SqliteConnection Connection = null!;
    protected ConnectionFactory Factory = null!;

    public async Task InitializeAsync()
    {
        Connection = new SqliteConnection("Data Source=:memory:");
        Connection.Open();

        Factory = new ConnectionFactory(Connection);

        var initializer = new SqliteDatabaseInitializer(Factory);
        await initializer.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        Connection.Dispose();
        return Task.CompletedTask;
    }
}
