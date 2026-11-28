using System.Data.Common;
using System.Globalization;
using Dapper;
using DividendTracker.Core.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace DividendTracker.Core.Features.Dividends;

public sealed class DividendService : IDividendService
{
    private const string DateFormat = "yyyy-MM-dd";

    private const string SelectBaseSql = """
        SELECT
            dp.Id,
            dp.HoldingId,
            h.PortfolioId,
            p.Name AS PortfolioName,
            h.AccountId,
            a.Name AS AccountName,
            h.SecurityId,
            s.Symbol AS SecuritySymbol,
            s.Name AS SecurityName,
            dp.PaymentDate,
            dp.ExDividendDate,
            dp.Amount,
            dp.IsReinvested,
            dp.Notes,
            mb.Id AS DripMarketBuyId,
            mb.NumberOfShares AS DripShares,
            mb.PricePerShare AS DripPricePerShare
        FROM DividendPayments dp
        INNER JOIN Holdings h ON h.Id = dp.HoldingId
        INNER JOIN Portfolios p ON p.Id = h.PortfolioId
        INNER JOIN Accounts a ON a.Id = h.AccountId
        INNER JOIN Securities s ON s.Id = h.SecurityId
        LEFT JOIN MarketBuys mb ON mb.DividendPaymentId = dp.Id
        """;

    private readonly ISqliteConnectionFactory _connectionFactory;

    public DividendService(ISqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<DividendPaymentDto>> GetDividendsAsync(
        long? portfolioId = null,
        long? holdingId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        const string sql = $"""
            {SelectBaseSql}
            WHERE (@PortfolioId IS NULL OR h.PortfolioId = @PortfolioId)
              AND (@HoldingId IS NULL OR dp.HoldingId = @HoldingId)
            ORDER BY dp.PaymentDate DESC, dp.Id DESC;
            """;

        var rows = await connection.QueryAsync<DividendPaymentRow>(
            new CommandDefinition(
                sql,
                new { PortfolioId = portfolioId, HoldingId = holdingId },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.Select(ToDto).ToList();
    }

    public async Task<DividendPaymentDto> RecordDividendAsync(
        RecordDividendRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (effectiveDripShares, effectiveDripPrice) = ValidateAndResolveDrip(
            request.Amount,
            request.IsReinvested,
            request.DripShares,
            request.DripPricePerShare);

        var notes = (request.Notes ?? string.Empty).Trim();
        var paymentDateText = request.PaymentDate.ToString(DateFormat, CultureInfo.InvariantCulture);
        var exDividendDateText = request.ExDividendDate?.ToString(DateFormat, CultureInfo.InvariantCulture);

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        const string verifyHoldingSql = "SELECT COUNT(1) FROM Holdings WHERE Id = @HoldingId;";
        var holdingExists = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                verifyHoldingSql,
                new { request.HoldingId },
                transaction: transaction,
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (holdingExists == 0)
        {
            throw new KeyNotFoundException($"Holding {request.HoldingId} was not found.");
        }

        const string insertDividendSql = """
            INSERT INTO DividendPayments (HoldingId, PaymentDate, ExDividendDate, Amount, IsReinvested, Notes)
            VALUES (@HoldingId, @PaymentDate, @ExDividendDate, @Amount, @IsReinvested, @Notes);
            SELECT last_insert_rowid();
            """;

        var dividendId = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                insertDividendSql,
                new
                {
                    request.HoldingId,
                    PaymentDate = paymentDateText,
                    ExDividendDate = exDividendDateText,
                    request.Amount,
                    IsReinvested = request.IsReinvested ? 1 : 0,
                    Notes = notes,
                },
                transaction: transaction,
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (request.IsReinvested &&
            effectiveDripShares is decimal dripShares &&
            effectiveDripPrice is decimal dripPrice)
        {
            const string insertDripBuySql = """
                INSERT INTO MarketBuys (HoldingId, NumberOfShares, PricePerShare, PurchaseDate, IsDrip, DividendPaymentId)
                VALUES (@HoldingId, @NumberOfShares, @PricePerShare, @PurchaseDate, 1, @DividendPaymentId);
                """;

            await connection.ExecuteAsync(
                new CommandDefinition(
                    insertDripBuySql,
                    new
                    {
                        request.HoldingId,
                        NumberOfShares = dripShares,
                        PricePerShare = dripPrice,
                        PurchaseDate = paymentDateText,
                        DividendPaymentId = dividendId,
                    },
                    transaction: transaction,
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        var result = await GetByIdAsync(connection, transaction, dividendId, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<DividendPaymentDto> UpdateDividendAsync(
        UpdateDividendRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (effectiveDripShares, effectiveDripPrice) = ValidateAndResolveDrip(
            request.Amount,
            request.IsReinvested,
            request.DripShares,
            request.DripPricePerShare);

        var notes = (request.Notes ?? string.Empty).Trim();
        var paymentDateText = request.PaymentDate.ToString(DateFormat, CultureInfo.InvariantCulture);
        var exDividendDateText = request.ExDividendDate?.ToString(DateFormat, CultureInfo.InvariantCulture);

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        const string lookupHoldingSql = "SELECT HoldingId FROM DividendPayments WHERE Id = @Id;";
        var holdingId = await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(
                lookupHoldingSql,
                new { request.Id },
                transaction: transaction,
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (holdingId is null)
        {
            throw new KeyNotFoundException($"Dividend payment {request.Id} was not found.");
        }

        const string updateDividendSql = """
            UPDATE DividendPayments
            SET PaymentDate = @PaymentDate,
                ExDividendDate = @ExDividendDate,
                Amount = @Amount,
                IsReinvested = @IsReinvested,
                Notes = @Notes
            WHERE Id = @Id;
            """;

        await connection.ExecuteAsync(
            new CommandDefinition(
                updateDividendSql,
                new
                {
                    request.Id,
                    PaymentDate = paymentDateText,
                    ExDividendDate = exDividendDateText,
                    request.Amount,
                    IsReinvested = request.IsReinvested ? 1 : 0,
                    Notes = notes,
                },
                transaction: transaction,
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (request.IsReinvested &&
            effectiveDripShares is decimal dripShares &&
            effectiveDripPrice is decimal dripPrice)
        {
            const string findDripBuySql = "SELECT Id FROM MarketBuys WHERE DividendPaymentId = @DividendPaymentId;";
            var existingBuyId = await connection.QuerySingleOrDefaultAsync<long?>(
                new CommandDefinition(
                    findDripBuySql,
                    new { DividendPaymentId = request.Id },
                    transaction: transaction,
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (existingBuyId is not null)
            {
                const string updateDripBuySql = """
                    UPDATE MarketBuys
                    SET NumberOfShares = @NumberOfShares,
                        PricePerShare = @PricePerShare,
                        PurchaseDate = @PurchaseDate,
                        IsDrip = 1
                    WHERE Id = @Id;
                    """;

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        updateDripBuySql,
                        new
                        {
                            Id = existingBuyId.Value,
                            NumberOfShares = dripShares,
                            PricePerShare = dripPrice,
                            PurchaseDate = paymentDateText,
                        },
                        transaction: transaction,
                        cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
            }
            else
            {
                const string insertDripBuySql = """
                    INSERT INTO MarketBuys (HoldingId, NumberOfShares, PricePerShare, PurchaseDate, IsDrip, DividendPaymentId)
                    VALUES (@HoldingId, @NumberOfShares, @PricePerShare, @PurchaseDate, 1, @DividendPaymentId);
                    """;

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        insertDripBuySql,
                        new
                        {
                            HoldingId = holdingId.Value,
                            NumberOfShares = dripShares,
                            PricePerShare = dripPrice,
                            PurchaseDate = paymentDateText,
                            DividendPaymentId = request.Id,
                        },
                        transaction: transaction,
                        cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
            }
        }
        else
        {
            const string deleteDripBuySql = "DELETE FROM MarketBuys WHERE DividendPaymentId = @DividendPaymentId;";
            await connection.ExecuteAsync(
                new CommandDefinition(
                    deleteDripBuySql,
                    new { DividendPaymentId = request.Id },
                    transaction: transaction,
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        var result = await GetByIdAsync(connection, transaction, request.Id, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<bool> DeleteDividendAsync(
        long dividendPaymentId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        const string deleteDripBuySql = "DELETE FROM MarketBuys WHERE DividendPaymentId = @Id;";
        await connection.ExecuteAsync(
            new CommandDefinition(
                deleteDripBuySql,
                new { Id = dividendPaymentId },
                transaction: transaction,
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        const string deleteDividendSql = "DELETE FROM DividendPayments WHERE Id = @Id;";
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                deleteDividendSql,
                new { Id = dividendPaymentId },
                transaction: transaction,
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    private static (decimal? DripShares, decimal? DripPricePerShare) ValidateAndResolveDrip(
        decimal amount,
        bool isReinvested,
        decimal? dripShares,
        decimal? dripPricePerShare)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        if (!isReinvested)
        {
            return (null, null);
        }

        if (dripPricePerShare is null or <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dripPricePerShare),
                "DRIP price per share must be greater than zero when IsReinvested is true.");
        }

        if (dripShares is <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dripShares),
                "DRIP shares must be greater than zero when provided.");
        }

        var resolvedShares = dripShares
            ?? Math.Round(amount / dripPricePerShare.Value, 6, MidpointRounding.AwayFromZero);

        return (resolvedShares, dripPricePerShare.Value);
    }

    private static async Task<DividendPaymentDto> GetByIdAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        long dividendPaymentId,
        CancellationToken cancellationToken)
    {
        const string sql = $"""
            {SelectBaseSql}
            WHERE dp.Id = @Id;
            """;

        var row = await connection.QuerySingleAsync<DividendPaymentRow>(
            new CommandDefinition(
                sql,
                new { Id = dividendPaymentId },
                transaction: transaction,
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return ToDto(row);
    }

    private static DividendPaymentDto ToDto(DividendPaymentRow row) =>
        new(
            row.Id,
            row.HoldingId,
            row.PortfolioId,
            row.PortfolioName,
            row.AccountId,
            row.AccountName,
            row.SecurityId,
            row.SecuritySymbol,
            row.SecurityName,
            DateOnly.ParseExact(row.PaymentDate, DateFormat, CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(row.ExDividendDate)
                ? null
                : DateOnly.ParseExact(row.ExDividendDate, DateFormat, CultureInfo.InvariantCulture),
            row.Amount,
            row.IsReinvested != 0,
            row.Notes,
            row.DripMarketBuyId,
            string.IsNullOrWhiteSpace(row.DripShares)
                ? null
                : decimal.Parse(row.DripShares, CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(row.DripPricePerShare)
                ? null
                : decimal.Parse(row.DripPricePerShare, CultureInfo.InvariantCulture));

    private sealed record DividendPaymentRow(
        long Id,
        long HoldingId,
        long PortfolioId,
        string PortfolioName,
        long AccountId,
        string AccountName,
        long SecurityId,
        string SecuritySymbol,
        string SecurityName,
        string PaymentDate,
        string? ExDividendDate,
        decimal Amount,
        long IsReinvested,
        string Notes,
        long? DripMarketBuyId,
        string? DripShares,
        string? DripPricePerShare);
}

