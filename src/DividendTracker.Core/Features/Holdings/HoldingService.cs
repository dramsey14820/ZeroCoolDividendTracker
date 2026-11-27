using System.Globalization;
using Dapper;
using DividendTracker.Core.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace DividendTracker.Core.Features.Holdings;

public sealed class HoldingService : IHoldingService
{
    private const string DateFormat = "yyyy-MM-dd";
    private const int SqliteConstraintErrorCode = 19;
    private const int SqliteConstraintUniqueExtendedErrorCode = 2067;

    private readonly ISqliteConnectionFactory _connectionFactory;

    public HoldingService(ISqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<HoldingDto>> GetHoldingsAsync(
        long? portfolioId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string holdingsSql = """
            SELECT
                h.Id,
                h.PortfolioId,
                p.Name AS PortfolioName,
                h.AccountId,
                a.Name AS AccountName,
                h.SecurityId,
                s.Symbol AS SecuritySymbol,
                s.Name AS SecurityName,
                s.LastPrice,
                h.IsDripEnabled
            FROM Holdings h
            INNER JOIN Portfolios p ON p.Id = h.PortfolioId
            INNER JOIN Accounts a ON a.Id = h.AccountId
            INNER JOIN Securities s ON s.Id = h.SecurityId
            WHERE (@PortfolioId IS NULL OR h.PortfolioId = @PortfolioId)
            ORDER BY p.Name, a.Name, s.Symbol, h.Id;
            """;

        const string buysSql = """
            SELECT
                mb.Id,
                mb.HoldingId,
                mb.NumberOfShares,
                mb.PricePerShare,
                mb.PurchaseDate,
                mb.IsDrip,
                mb.DividendPaymentId
            FROM MarketBuys mb
            INNER JOIN Holdings h ON h.Id = mb.HoldingId
            WHERE (@PortfolioId IS NULL OR h.PortfolioId = @PortfolioId)
            ORDER BY mb.PurchaseDate DESC, mb.Id DESC;
            """;

        var holdingRows = (await connection.QueryAsync<HoldingRow>(
            new CommandDefinition(holdingsSql, new { PortfolioId = portfolioId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false)).ToList();

        if (holdingRows.Count == 0)
        {
            return Array.Empty<HoldingDto>();
        }

        var buyRows = await connection.QueryAsync<MarketBuyRow>(
            new CommandDefinition(buysSql, new { PortfolioId = portfolioId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var buysByHolding = buyRows
            .Select(ToMarketBuyDto)
            .GroupBy(b => b.HoldingId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<MarketBuyDto>)g.ToList());

        return holdingRows
            .Select(row => ToHoldingDto(
                row,
                buysByHolding.TryGetValue(row.Id, out var buys) ? buys : Array.Empty<MarketBuyDto>()))
            .ToList();
    }

    public async Task<HoldingDto> CreateHoldingAsync(
        CreateHoldingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string verifyAccountSql = """
            SELECT COUNT(1)
            FROM Accounts
            WHERE Id = @AccountId AND PortfolioId = @PortfolioId;
            """;

        var accountMatches = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                verifyAccountSql,
                new { request.AccountId, request.PortfolioId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (accountMatches == 0)
        {
            throw new KeyNotFoundException(
                $"Account {request.AccountId} was not found under portfolio {request.PortfolioId}.");
        }

        const string verifySecuritySql = "SELECT COUNT(1) FROM Securities WHERE Id = @SecurityId;";
        var securityExists = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                verifySecuritySql,
                new { request.SecurityId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (securityExists == 0)
        {
            throw new KeyNotFoundException($"Security {request.SecurityId} was not found.");
        }

        const string insertSql = """
            INSERT INTO Holdings (PortfolioId, AccountId, SecurityId, IsDripEnabled)
            VALUES (@PortfolioId, @AccountId, @SecurityId, @IsDripEnabled);
            SELECT last_insert_rowid();
            """;

        long newId;
        try
        {
            newId = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    insertSql,
                    new
                    {
                        request.PortfolioId,
                        request.AccountId,
                        request.SecurityId,
                        IsDripEnabled = request.IsDripEnabled ? 1 : 0,
                    },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (
            ex.SqliteExtendedErrorCode == SqliteConstraintUniqueExtendedErrorCode ||
            ex.SqliteErrorCode == SqliteConstraintErrorCode)
        {
            throw new InvalidOperationException(
                "A holding for this portfolio, account, and security already exists.",
                ex);
        }

        return await GetHoldingByIdAsync(connection, newId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HoldingDto> UpdateHoldingAsync(
        UpdateHoldingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string updateSql = """
            UPDATE Holdings
            SET IsDripEnabled = @IsDripEnabled
            WHERE Id = @Id;
            """;

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                updateSql,
                new
                {
                    request.Id,
                    IsDripEnabled = request.IsDripEnabled ? 1 : 0,
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new KeyNotFoundException($"Holding {request.Id} was not found.");
        }

        return await GetHoldingByIdAsync(connection, request.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteHoldingAsync(
        long holdingId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string deleteSql = "DELETE FROM Holdings WHERE Id = @Id;";
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(deleteSql, new { Id = holdingId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return affected > 0;
    }

    public async Task<MarketBuyDto> CreateMarketBuyAsync(
        CreateMarketBuyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.NumberOfShares);
        ArgumentOutOfRangeException.ThrowIfNegative(request.PricePerShare);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string verifyHoldingSql = "SELECT COUNT(1) FROM Holdings WHERE Id = @HoldingId;";
        var holdingExists = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(verifyHoldingSql, new { request.HoldingId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (holdingExists == 0)
        {
            throw new KeyNotFoundException($"Holding {request.HoldingId} was not found.");
        }

        const string insertSql = """
            INSERT INTO MarketBuys (HoldingId, NumberOfShares, PricePerShare, PurchaseDate, IsDrip, DividendPaymentId)
            VALUES (@HoldingId, @NumberOfShares, @PricePerShare, @PurchaseDate, @IsDrip, NULL);
            SELECT last_insert_rowid();
            """;

        var newId = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                insertSql,
                new
                {
                    request.HoldingId,
                    request.NumberOfShares,
                    request.PricePerShare,
                    PurchaseDate = request.PurchaseDate.ToString(DateFormat, CultureInfo.InvariantCulture),
                    IsDrip = request.IsDrip ? 1 : 0,
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return new MarketBuyDto(
            newId,
            request.HoldingId,
            request.NumberOfShares,
            request.PricePerShare,
            request.PurchaseDate,
            request.IsDrip,
            null);
    }

    public async Task<MarketBuyDto> UpdateMarketBuyAsync(
        UpdateMarketBuyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.NumberOfShares);
        ArgumentOutOfRangeException.ThrowIfNegative(request.PricePerShare);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string updateSql = """
            UPDATE MarketBuys
            SET NumberOfShares = @NumberOfShares,
                PricePerShare = @PricePerShare,
                PurchaseDate = @PurchaseDate,
                IsDrip = @IsDrip
            WHERE Id = @Id;
            """;

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                updateSql,
                new
                {
                    request.Id,
                    request.NumberOfShares,
                    request.PricePerShare,
                    PurchaseDate = request.PurchaseDate.ToString(DateFormat, CultureInfo.InvariantCulture),
                    IsDrip = request.IsDrip ? 1 : 0,
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new KeyNotFoundException($"Market buy {request.Id} was not found.");
        }

        const string selectSql = """
            SELECT
                Id,
                HoldingId,
                NumberOfShares,
                PricePerShare,
                PurchaseDate,
                IsDrip,
                DividendPaymentId
            FROM MarketBuys
            WHERE Id = @Id;
            """;

        var row = await connection.QuerySingleAsync<MarketBuyRow>(
            new CommandDefinition(selectSql, new { request.Id }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return ToMarketBuyDto(row);
    }

    public async Task<bool> DeleteMarketBuyAsync(
        long marketBuyId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string deleteSql = "DELETE FROM MarketBuys WHERE Id = @Id;";
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(deleteSql, new { Id = marketBuyId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return affected > 0;
    }

    private static async Task<HoldingDto> GetHoldingByIdAsync(
        SqliteConnection connection,
        long holdingId,
        CancellationToken cancellationToken)
    {
        const string holdingSql = """
            SELECT
                h.Id,
                h.PortfolioId,
                p.Name AS PortfolioName,
                h.AccountId,
                a.Name AS AccountName,
                h.SecurityId,
                s.Symbol AS SecuritySymbol,
                s.Name AS SecurityName,
                s.LastPrice,
                h.IsDripEnabled
            FROM Holdings h
            INNER JOIN Portfolios p ON p.Id = h.PortfolioId
            INNER JOIN Accounts a ON a.Id = h.AccountId
            INNER JOIN Securities s ON s.Id = h.SecurityId
            WHERE h.Id = @Id;
            """;

        const string buysSql = """
            SELECT
                Id,
                HoldingId,
                NumberOfShares,
                PricePerShare,
                PurchaseDate,
                IsDrip,
                DividendPaymentId
            FROM MarketBuys
            WHERE HoldingId = @HoldingId
            ORDER BY PurchaseDate DESC, Id DESC;
            """;

        var row = await connection.QuerySingleAsync<HoldingRow>(
            new CommandDefinition(holdingSql, new { Id = holdingId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var buys = (await connection.QueryAsync<MarketBuyRow>(
            new CommandDefinition(buysSql, new { HoldingId = holdingId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false))
            .Select(ToMarketBuyDto)
            .ToList();

        return ToHoldingDto(row, buys);
    }

    private static HoldingDto ToHoldingDto(HoldingRow row, IReadOnlyList<MarketBuyDto> buys)
    {
        var totalShares = buys.Sum(b => b.NumberOfShares);
        var totalCostBasis = buys.Sum(b => b.TotalCost);
        var averageCost = totalShares > 0m ? totalCostBasis / totalShares : 0m;
        var marketValue = totalShares * row.LastPrice;

        return new HoldingDto(
            row.Id,
            row.PortfolioId,
            row.PortfolioName,
            row.AccountId,
            row.AccountName,
            row.SecurityId,
            row.SecuritySymbol,
            row.SecurityName,
            row.LastPrice,
            row.IsDripEnabled != 0,
            totalShares,
            totalCostBasis,
            averageCost,
            marketValue,
            buys);
    }

    private static MarketBuyDto ToMarketBuyDto(MarketBuyRow row) =>
        new(
            row.Id,
            row.HoldingId,
            row.NumberOfShares,
            row.PricePerShare,
            DateOnly.ParseExact(row.PurchaseDate, DateFormat, CultureInfo.InvariantCulture),
            row.IsDrip != 0,
            row.DividendPaymentId);

    private sealed record HoldingRow(
        long Id,
        long PortfolioId,
        string PortfolioName,
        long AccountId,
        string AccountName,
        long SecurityId,
        string SecuritySymbol,
        string SecurityName,
        decimal LastPrice,
        long IsDripEnabled);

    private sealed record MarketBuyRow(
        long Id,
        long HoldingId,
        decimal NumberOfShares,
        decimal PricePerShare,
        string PurchaseDate,
        long IsDrip,
        long? DividendPaymentId);
}

