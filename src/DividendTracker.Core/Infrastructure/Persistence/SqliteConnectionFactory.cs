using Dapper;
using Microsoft.Data.Sqlite;

namespace DividendTracker.Core.Infrastructure.Persistence;

public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly SqliteOptions _options;

    public SqliteConnectionFactory(SqliteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
        _options = options;
    }

    public async Task<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var builder = new SqliteConnectionStringBuilder(_options.ConnectionString);
        if (!string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(builder.DataSource);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        var connection = new SqliteConnection(_options.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await connection.ExecuteAsync(
                new CommandDefinition("PRAGMA foreign_keys = ON;", cancellationToken: cancellationToken))
                .ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

