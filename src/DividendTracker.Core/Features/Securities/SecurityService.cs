using System.Globalization;
using Dapper;
using DividendTracker.Core.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace DividendTracker.Core.Features.Securities;

public sealed class SecurityService : ISecurityService
{
    private const int SqliteConstraintErrorCode = 19;
    private const int MaxLookupNameLength = 100;
    private const int MaxSymbolLength = 20;
    private const int MaxSecurityNameLength = 150;

    private const string SelectSecuritiesBaseSql = """
        SELECT
            s.Id,
            s.Symbol,
            s.Name,
            s.InvestmentTypeId,
            inv.Name AS InvestmentTypeName,
            s.IndustryTypeId,
            ind.Name AS IndustryTypeName,
            s.SectorTypeId,
            sec.Name AS SectorTypeName,
            s.LastPrice,
            s.LastPriceUpdatedUtc
        FROM Securities s
        INNER JOIN InvestmentTypes inv ON inv.Id = s.InvestmentTypeId
        INNER JOIN IndustryTypes ind ON ind.Id = s.IndustryTypeId
        INNER JOIN SectorTypes sec ON sec.Id = s.SectorTypeId
        """;

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;

    public SecurityService(ISqliteConnectionFactory connectionFactory, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public async Task<LookupCatalogDto> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var investmentTypes = (await connection.QueryAsync<LookupItemDto>(new CommandDefinition(
            "SELECT Id, Name FROM InvestmentTypes ORDER BY Name, Id;",
            cancellationToken: cancellationToken))).AsList();

        var industryTypes = (await connection.QueryAsync<LookupItemDto>(new CommandDefinition(
            "SELECT Id, Name FROM IndustryTypes ORDER BY Name, Id;",
            cancellationToken: cancellationToken))).AsList();

        var sectorTypes = (await connection.QueryAsync<LookupItemDto>(new CommandDefinition(
            "SELECT Id, Name FROM SectorTypes ORDER BY Name, Id;",
            cancellationToken: cancellationToken))).AsList();

        return new LookupCatalogDto(investmentTypes, industryTypes, sectorTypes);
    }

    public async Task<LookupItemDto> CreateLookupAsync(
        CreateLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = ValidateRequiredText(request.Name, MaxLookupNameLength, nameof(request.Name));
        var (checkDuplicateSql, insertSql) = GetLookupInsertSql(request.Category);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var existingCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            checkDuplicateSql,
            new { Name = name },
            cancellationToken: cancellationToken));

        if (existingCount > 0)
        {
            throw new InvalidOperationException($"Lookup item '{name}' already exists.");
        }

        try
        {
            var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                insertSql,
                new { Name = name },
                cancellationToken: cancellationToken));

            return new LookupItemDto(id, name);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintErrorCode)
        {
            throw new InvalidOperationException($"Lookup item '{name}' already exists.", ex);
        }
    }

    public async Task<LookupItemDto> UpdateLookupAsync(
        UpdateLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = ValidateRequiredText(request.Name, MaxLookupNameLength, nameof(request.Name));
        var (checkDuplicateSql, updateSql) = GetLookupUpdateSql(request.Category);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var duplicateCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            checkDuplicateSql,
            new { request.Id, Name = name },
            cancellationToken: cancellationToken));

        if (duplicateCount > 0)
        {
            throw new InvalidOperationException($"Lookup item '{name}' already exists.");
        }

        try
        {
            var affected = await connection.ExecuteAsync(new CommandDefinition(
                updateSql,
                new { request.Id, Name = name },
                cancellationToken: cancellationToken));

            if (affected == 0)
            {
                throw new KeyNotFoundException($"Lookup item with ID {request.Id} was not found.");
            }

            return new LookupItemDto(request.Id, name);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintErrorCode)
        {
            throw new InvalidOperationException($"Lookup item '{name}' already exists.", ex);
        }
    }

    public async Task<bool> DeleteLookupAsync(
        LookupCategory category,
        long id,
        CancellationToken cancellationToken = default)
    {
        var deleteSql = GetLookupDeleteSql(category);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        try
        {
            var affected = await connection.ExecuteAsync(new CommandDefinition(
                deleteSql,
                new { Id = id },
                cancellationToken: cancellationToken));

            return affected > 0;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintErrorCode)
        {
            throw new InvalidOperationException(
                "Cannot delete lookup item that is referenced by one or more securities.",
                ex);
        }
    }

    public async Task<IReadOnlyList<SecurityDto>> GetSecuritiesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<SecurityDto>(new CommandDefinition(
            $"{SelectSecuritiesBaseSql} ORDER BY s.Symbol, s.Id;",
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<SecurityDto> CreateSecurityAsync(
        CreateSecurityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var symbol = ValidateAndNormalizeSymbol(request.Symbol, nameof(request.Symbol));
        var name = ValidateRequiredText(request.Name, MaxSecurityNameLength, nameof(request.Name));
        ArgumentOutOfRangeException.ThrowIfNegative(request.LastPrice);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        await EnsureTaxonomiesExistAsync(
            connection,
            request.InvestmentTypeId,
            request.IndustryTypeId,
            request.SectorTypeId,
            cancellationToken);

        var lastPriceUpdatedUtc = request.LastPrice > 0m
            ? _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)
            : null;

        try
        {
            var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                INSERT INTO Securities
                    (Symbol, Name, InvestmentTypeId, IndustryTypeId, SectorTypeId, LastPrice, LastPriceUpdatedUtc)
                VALUES
                    (@Symbol, @Name, @InvestmentTypeId, @IndustryTypeId, @SectorTypeId, @LastPrice, @LastPriceUpdatedUtc);
                SELECT last_insert_rowid();
                """,
                new
                {
                    Symbol = symbol,
                    Name = name,
                    request.InvestmentTypeId,
                    request.IndustryTypeId,
                    request.SectorTypeId,
                    request.LastPrice,
                    LastPriceUpdatedUtc = lastPriceUpdatedUtc,
                },
                cancellationToken: cancellationToken));

            return await GetSecurityByIdAsync(connection, id, cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintErrorCode)
        {
            throw new InvalidOperationException($"A security with symbol '{symbol}' already exists.", ex);
        }
    }

    public async Task<SecurityDto> UpdateSecurityAsync(
        UpdateSecurityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var symbol = ValidateAndNormalizeSymbol(request.Symbol, nameof(request.Symbol));
        var name = ValidateRequiredText(request.Name, MaxSecurityNameLength, nameof(request.Name));
        ArgumentOutOfRangeException.ThrowIfNegative(request.LastPrice);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        await EnsureTaxonomiesExistAsync(
            connection,
            request.InvestmentTypeId,
            request.IndustryTypeId,
            request.SectorTypeId,
            cancellationToken);

        var lastPriceUpdatedUtc = request.LastPrice > 0m
            ? _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)
            : null;

        try
        {
            var affected = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE Securities
                SET Symbol = @Symbol,
                    Name = @Name,
                    InvestmentTypeId = @InvestmentTypeId,
                    IndustryTypeId = @IndustryTypeId,
                    SectorTypeId = @SectorTypeId,
                    LastPrice = @LastPrice,
                    LastPriceUpdatedUtc = @LastPriceUpdatedUtc
                WHERE Id = @Id;
                """,
                new
                {
                    request.Id,
                    Symbol = symbol,
                    Name = name,
                    request.InvestmentTypeId,
                    request.IndustryTypeId,
                    request.SectorTypeId,
                    request.LastPrice,
                    LastPriceUpdatedUtc = lastPriceUpdatedUtc,
                },
                cancellationToken: cancellationToken));

            if (affected == 0)
            {
                throw new KeyNotFoundException($"Security with ID {request.Id} was not found.");
            }

            return await GetSecurityByIdAsync(connection, request.Id, cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintErrorCode)
        {
            throw new InvalidOperationException($"A security with symbol '{symbol}' already exists.", ex);
        }
    }

    public async Task<bool> DeleteSecurityAsync(long securityId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        try
        {
            var affected = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM Securities WHERE Id = @Id;",
                new { Id = securityId },
                cancellationToken: cancellationToken));

            return affected > 0;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintErrorCode)
        {
            throw new InvalidOperationException(
                "Cannot delete security that is referenced by one or more holdings.",
                ex);
        }
    }

    private static async Task EnsureTaxonomiesExistAsync(
        SqliteConnection connection,
        long investmentTypeId,
        long industryTypeId,
        long sectorTypeId,
        CancellationToken cancellationToken)
    {
        var investmentExists = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(1) FROM InvestmentTypes WHERE Id = @Id;",
            new { Id = investmentTypeId },
            cancellationToken: cancellationToken));

        if (investmentExists == 0)
        {
            throw new KeyNotFoundException($"InvestmentType with ID {investmentTypeId} was not found.");
        }

        var industryExists = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(1) FROM IndustryTypes WHERE Id = @Id;",
            new { Id = industryTypeId },
            cancellationToken: cancellationToken));

        if (industryExists == 0)
        {
            throw new KeyNotFoundException($"IndustryType with ID {industryTypeId} was not found.");
        }

        var sectorExists = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(1) FROM SectorTypes WHERE Id = @Id;",
            new { Id = sectorTypeId },
            cancellationToken: cancellationToken));

        if (sectorExists == 0)
        {
            throw new KeyNotFoundException($"SectorType with ID {sectorTypeId} was not found.");
        }
    }

    private static async Task<SecurityDto> GetSecurityByIdAsync(
        SqliteConnection connection,
        long id,
        CancellationToken cancellationToken)
    {
        var security = await connection.QuerySingleOrDefaultAsync<SecurityDto>(new CommandDefinition(
            $"{SelectSecuritiesBaseSql} WHERE s.Id = @Id;",
            new { Id = id },
            cancellationToken: cancellationToken));

        return security ?? throw new KeyNotFoundException($"Security with ID {id} was not found.");
    }

    private static string ValidateRequiredText(string value, int maxLength, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        var trimmed = value.Trim();

        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"Value must not exceed {maxLength} characters.", paramName);
        }

        return trimmed;
    }

    private static string ValidateAndNormalizeSymbol(string symbol, string paramName)
    {
        var trimmed = ValidateRequiredText(symbol, MaxSymbolLength, paramName);
        return trimmed.ToUpperInvariant();
    }

    private static (string CheckDuplicateSql, string InsertSql) GetLookupInsertSql(LookupCategory category) =>
        category switch
        {
            LookupCategory.InvestmentType => (
                "SELECT COUNT(1) FROM InvestmentTypes WHERE Name = @Name COLLATE NOCASE;",
                "INSERT INTO InvestmentTypes (Name) VALUES (@Name); SELECT last_insert_rowid();"),
            LookupCategory.IndustryType => (
                "SELECT COUNT(1) FROM IndustryTypes WHERE Name = @Name COLLATE NOCASE;",
                "INSERT INTO IndustryTypes (Name) VALUES (@Name); SELECT last_insert_rowid();"),
            LookupCategory.SectorType => (
                "SELECT COUNT(1) FROM SectorTypes WHERE Name = @Name COLLATE NOCASE;",
                "INSERT INTO SectorTypes (Name) VALUES (@Name); SELECT last_insert_rowid();"),
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unsupported lookup category."),
        };

    private static (string CheckDuplicateSql, string UpdateSql) GetLookupUpdateSql(LookupCategory category) =>
        category switch
        {
            LookupCategory.InvestmentType => (
                "SELECT COUNT(1) FROM InvestmentTypes WHERE Name = @Name COLLATE NOCASE AND Id <> @Id;",
                "UPDATE InvestmentTypes SET Name = @Name WHERE Id = @Id;"),
            LookupCategory.IndustryType => (
                "SELECT COUNT(1) FROM IndustryTypes WHERE Name = @Name COLLATE NOCASE AND Id <> @Id;",
                "UPDATE IndustryTypes SET Name = @Name WHERE Id = @Id;"),
            LookupCategory.SectorType => (
                "SELECT COUNT(1) FROM SectorTypes WHERE Name = @Name COLLATE NOCASE AND Id <> @Id;",
                "UPDATE SectorTypes SET Name = @Name WHERE Id = @Id;"),
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unsupported lookup category."),
        };

    private static string GetLookupDeleteSql(LookupCategory category) =>
        category switch
        {
            LookupCategory.InvestmentType => "DELETE FROM InvestmentTypes WHERE Id = @Id;",
            LookupCategory.IndustryType => "DELETE FROM IndustryTypes WHERE Id = @Id;",
            LookupCategory.SectorType => "DELETE FROM SectorTypes WHERE Id = @Id;",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unsupported lookup category."),
        };
}

