using System.Globalization;
using Dapper;
using DividendTracker.Core.Infrastructure.Persistence;

namespace DividendTracker.Core.Features.Portfolios;

public sealed class PortfolioService : IPortfolioService
{
    private const int MaxNameLength = 100;
    private const int MaxAccountTypeLength = 50;

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;

    public PortfolioService(ISqliteConnectionFactory connectionFactory, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<PortfolioDto>> GetPortfoliosAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var portfolioRows = (await connection.QueryAsync<PortfolioRow>(
            new CommandDefinition(
                "SELECT Id, Name, Description, IsModelled, CreatedUtc FROM Portfolios ORDER BY Name, Id;",
                cancellationToken: cancellationToken))).ToList();

        var accountRows = (await connection.QueryAsync<AccountRow>(
            new CommandDefinition(
                "SELECT Id, PortfolioId, Name, AccountType, CreatedUtc FROM Accounts ORDER BY Name, Id;",
                cancellationToken: cancellationToken))).ToList();

        var accountsByPortfolio = accountRows
            .Select(ToAccountDto)
            .ToLookup(a => a.PortfolioId);

        return portfolioRows
            .Select(p => new PortfolioDto(
                p.Id,
                p.Name,
                p.Description,
                p.IsModelled != 0,
                p.CreatedUtc,
                accountsByPortfolio[p.Id].ToList()))
            .ToList();
    }

    public async Task<PortfolioDto> CreatePortfolioAsync(
        CreatePortfolioRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = ValidateRequiredText(request.Name, MaxNameLength, nameof(request.Name));
        var description = (request.Description ?? string.Empty).Trim();
        var createdUtc = _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                """
                INSERT INTO Portfolios (Name, Description, IsModelled, CreatedUtc)
                VALUES (@Name, @Description, @IsModelled, @CreatedUtc);
                SELECT last_insert_rowid();
                """,
                new
                {
                    Name = name,
                    Description = description,
                    IsModelled = request.IsModelled ? 1 : 0,
                    CreatedUtc = createdUtc
                },
                cancellationToken: cancellationToken));

        return new PortfolioDto(id, name, description, request.IsModelled, createdUtc, Array.Empty<AccountDto>());
    }

    public async Task<PortfolioDto> UpdatePortfolioAsync(
        UpdatePortfolioRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = ValidateRequiredText(request.Name, MaxNameLength, nameof(request.Name));
        var description = (request.Description ?? string.Empty).Trim();

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE Portfolios
                SET Name = @Name, Description = @Description, IsModelled = @IsModelled
                WHERE Id = @Id;
                """,
                new
                {
                    request.Id,
                    Name = name,
                    Description = description,
                    IsModelled = request.IsModelled ? 1 : 0
                },
                cancellationToken: cancellationToken));

        if (affected == 0)
        {
            throw new KeyNotFoundException($"Portfolio with Id {request.Id} was not found.");
        }

        var updated = await connection.QuerySingleAsync<PortfolioRow>(
            new CommandDefinition(
                "SELECT Id, Name, Description, IsModelled, CreatedUtc FROM Portfolios WHERE Id = @Id;",
                new { request.Id },
                cancellationToken: cancellationToken));

        var accounts = (await connection.QueryAsync<AccountRow>(
            new CommandDefinition(
                "SELECT Id, PortfolioId, Name, AccountType, CreatedUtc FROM Accounts WHERE PortfolioId = @PortfolioId ORDER BY Name, Id;",
                new { PortfolioId = request.Id },
                cancellationToken: cancellationToken)))
            .Select(ToAccountDto)
            .ToList();

        return new PortfolioDto(
            updated.Id,
            updated.Name,
            updated.Description,
            updated.IsModelled != 0,
            updated.CreatedUtc,
            accounts);
    }

    public async Task<bool> DeletePortfolioAsync(long portfolioId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM Portfolios WHERE Id = @Id;",
                new { Id = portfolioId },
                cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<AccountDto> CreateAccountAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = ValidateRequiredText(request.Name, MaxNameLength, nameof(request.Name));
        var accountType = ValidateRequiredText(request.AccountType, MaxAccountTypeLength, nameof(request.AccountType));
        var createdUtc = _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var portfolioExists = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                "SELECT COUNT(1) FROM Portfolios WHERE Id = @PortfolioId;",
                new { request.PortfolioId },
                cancellationToken: cancellationToken));

        if (portfolioExists == 0)
        {
            throw new KeyNotFoundException($"Portfolio with Id {request.PortfolioId} was not found.");
        }

        var id = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                """
                INSERT INTO Accounts (PortfolioId, Name, AccountType, CreatedUtc)
                VALUES (@PortfolioId, @Name, @AccountType, @CreatedUtc);
                SELECT last_insert_rowid();
                """,
                new
                {
                    request.PortfolioId,
                    Name = name,
                    AccountType = accountType,
                    CreatedUtc = createdUtc
                },
                cancellationToken: cancellationToken));

        return new AccountDto(id, request.PortfolioId, name, accountType, createdUtc);
    }

    public async Task<AccountDto> UpdateAccountAsync(
        UpdateAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = ValidateRequiredText(request.Name, MaxNameLength, nameof(request.Name));
        var accountType = ValidateRequiredText(request.AccountType, MaxAccountTypeLength, nameof(request.AccountType));

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE Accounts
                SET Name = @Name, AccountType = @AccountType
                WHERE Id = @Id;
                """,
                new
                {
                    request.Id,
                    Name = name,
                    AccountType = accountType
                },
                cancellationToken: cancellationToken));

        if (affected == 0)
        {
            throw new KeyNotFoundException($"Account with Id {request.Id} was not found.");
        }

        var updated = await connection.QuerySingleAsync<AccountRow>(
            new CommandDefinition(
                "SELECT Id, PortfolioId, Name, AccountType, CreatedUtc FROM Accounts WHERE Id = @Id;",
                new { request.Id },
                cancellationToken: cancellationToken));

        return ToAccountDto(updated);
    }

    public async Task<bool> DeleteAccountAsync(long accountId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM Accounts WHERE Id = @Id;",
                new { Id = accountId },
                cancellationToken: cancellationToken));

        return affected > 0;
    }

    private static string ValidateRequiredText(string value, int maxLength, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{paramName} must be {maxLength} characters or fewer.", paramName);
        }

        return trimmed;
    }

    private static AccountDto ToAccountDto(AccountRow row) =>
        new(row.Id, row.PortfolioId, row.Name, row.AccountType, row.CreatedUtc);

    private sealed record PortfolioRow(
        long Id,
        string Name,
        string Description,
        long IsModelled,
        string CreatedUtc);

    private sealed record AccountRow(
        long Id,
        long PortfolioId,
        string Name,
        string AccountType,
        string CreatedUtc);
}

