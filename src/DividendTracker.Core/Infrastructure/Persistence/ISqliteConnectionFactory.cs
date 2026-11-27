using Microsoft.Data.Sqlite;

namespace DividendTracker.Core.Infrastructure.Persistence;

public interface ISqliteConnectionFactory
{
    Task<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}

