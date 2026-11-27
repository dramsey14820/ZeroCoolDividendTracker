using System.Data;
using Dapper;
using DividendTracker.Core.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DividendTracker.Tests.Infrastructure.Persistence;

public sealed class SqliteConnectionFactoryTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"zcdt_conn_tests_{Guid.NewGuid():N}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateOpenConnectionAsync_WithValidTempPath_OpensConnectionWithForeignKeysAndCreatesDirectory()
    {
        // Arrange
        var dbPath = Path.Combine(_tempDirectory, "nested", "test.db");
        var options = new SqliteOptions($"Data Source={dbPath}");
        var factory = new SqliteConnectionFactory(options);

        // Act
        await using var connection = await factory.CreateOpenConnectionAsync();
        var foreignKeysEnabled = await connection.ExecuteScalarAsync<long>("PRAGMA foreign_keys;");

        // Assert
        connection.State.Should().Be(ConnectionState.Open);
        foreignKeysEnabled.Should().Be(1);
        Directory.Exists(Path.GetDirectoryName(dbPath)!).Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithBlankConnectionString_ThrowsArgumentException()
    {
        // Arrange
        var options = new SqliteOptions("   ");

        // Act
        var act = () => new SqliteConnectionFactory(options);

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}

