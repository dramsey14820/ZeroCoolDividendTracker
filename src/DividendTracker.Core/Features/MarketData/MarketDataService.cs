using System.Globalization;
using System.Text.Json;
using Dapper;
using DividendTracker.Core.Infrastructure.Persistence;

namespace DividendTracker.Core.Features.MarketData;

public sealed class MarketDataService : IMarketDataService
{
    private const string ProviderSettingKey = "MarketData.Provider";
    private const string ApiKeySettingKey = "MarketData.ApiKey";
    private const string CacheTtlSettingKey = "MarketData.CacheTtlMinutes";
    private const int DefaultCacheTtlMinutes = 15;

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly Dictionary<MarketDataProviderType, IMarketDataProvider> _providers;
    private readonly TimeProvider _timeProvider;

    public MarketDataService(
        ISqliteConnectionFactory connectionFactory,
        IEnumerable<IMarketDataProvider> providers,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(providers);

        _connectionFactory = connectionFactory;
        _providers = providers.ToDictionary(p => p.ProviderType);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<MarketDataSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = (await connection.QueryAsync<SettingRow>(
            new CommandDefinition(
                """
                SELECT Key, Value
                FROM AppSettings
                WHERE Key IN (@ProviderKey, @ApiKey, @CacheTtlKey);
                """,
                new
                {
                    ProviderKey = ProviderSettingKey,
                    ApiKey = ApiKeySettingKey,
                    CacheTtlKey = CacheTtlSettingKey,
                },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false)).ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);

        var provider = MarketDataProviderType.YahooFinance;
        if (rows.TryGetValue(ProviderSettingKey, out var providerText) &&
            Enum.TryParse<MarketDataProviderType>(providerText, ignoreCase: true, out var parsedProvider) &&
            Enum.IsDefined(parsedProvider))
        {
            provider = parsedProvider;
        }

        var apiKey = rows.TryGetValue(ApiKeySettingKey, out var storedKey)
            ? storedKey
            : string.Empty;

        var cacheTtl = DefaultCacheTtlMinutes;
        if (rows.TryGetValue(CacheTtlSettingKey, out var ttlText) &&
            int.TryParse(ttlText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTtl) &&
            parsedTtl is >= 1 and <= 1440)
        {
            cacheTtl = parsedTtl;
        }

        return new MarketDataSettingsDto(provider, apiKey, cacheTtl);
    }

    public async Task<MarketDataSettingsDto> SaveSettingsAsync(
        UpdateMarketDataSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.IsDefined(request.Provider))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Unknown market data provider.");
        }

        if (request.CacheTtlMinutes is < 1 or > 1440)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Cache TTL must be between 1 and 1440 minutes.");
        }

        var normalizedKey = request.ApiKey?.Trim() ?? string.Empty;
        var updatedUtc = _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        const string upsertSql = """
            INSERT INTO AppSettings (Key, Value, UpdatedUtc)
            VALUES (@Key, @Value, @UpdatedUtc)
            ON CONFLICT(Key) DO UPDATE SET
                Value = excluded.Value,
                UpdatedUtc = excluded.UpdatedUtc;
            """;

        await connection.ExecuteAsync(
            new CommandDefinition(
                upsertSql,
                new[]
                {
                    new { Key = ProviderSettingKey, Value = request.Provider.ToString(), UpdatedUtc = updatedUtc },
                    new { Key = ApiKeySettingKey, Value = normalizedKey, UpdatedUtc = updatedUtc },
                    new { Key = CacheTtlSettingKey, Value = request.CacheTtlMinutes.ToString(CultureInfo.InvariantCulture), UpdatedUtc = updatedUtc },
                },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return new MarketDataSettingsDto(request.Provider, normalizedKey, request.CacheTtlMinutes);
    }

    public async Task<MarketQuoteDto> RefreshSecurityQuoteAsync(
        long securityId,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(securityId);

        var settings = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        var provider = ResolveProvider(settings.Provider);

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var security = await connection.QuerySingleOrDefaultAsync<SecurityQuoteRow>(
            new CommandDefinition(
                """
                SELECT Id, Symbol, Name, LastPrice, LastPriceUpdatedUtc
                FROM Securities
                WHERE Id = @SecurityId;
                """,
                new { SecurityId = securityId },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (security is null)
        {
            throw new KeyNotFoundException($"Security {securityId} was not found.");
        }

        var nowUtc = _timeProvider.GetUtcNow();
        if (!forceRefresh &&
            !string.IsNullOrWhiteSpace(security.LastPriceUpdatedUtc) &&
            DateTimeOffset.TryParse(security.LastPriceUpdatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastUpdatedUtc) &&
            (nowUtc - lastUpdatedUtc) < TimeSpan.FromMinutes(settings.CacheTtlMinutes))
        {
            return new MarketQuoteDto(
                security.Symbol,
                security.LastPrice,
                security.Name,
                provider.DisplayName,
                lastUpdatedUtc,
                FromCache: true);
        }

        var quote = await provider.GetQuoteAsync(security.Symbol, settings.ApiKey, cancellationToken).ConfigureAwait(false);
        var updatedUtcText = nowUtc.ToString("O", CultureInfo.InvariantCulture);

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE Securities
                SET LastPrice = @LastPrice,
                    LastPriceUpdatedUtc = @LastPriceUpdatedUtc
                WHERE Id = @SecurityId;
                """,
                new
                {
                    LastPrice = quote.Price,
                    LastPriceUpdatedUtc = updatedUtcText,
                    SecurityId = securityId,
                },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return quote with { RetrievedUtc = nowUtc, FromCache = false };
    }

    public async Task<MarketDataRefreshResultDto> RefreshAllQuotesAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var securities = (await connection.QueryAsync<SecuritySummaryRow>(
            new CommandDefinition(
                "SELECT Id, Symbol FROM Securities ORDER BY Symbol ASC;",
                cancellationToken: cancellationToken))
            .ConfigureAwait(false)).ToList();

        var updatedCount = 0;
        var cachedCount = 0;
        var failedCount = 0;
        var errors = new List<string>();

        foreach (var security in securities)
        {
            try
            {
                var quote = await RefreshSecurityQuoteAsync(security.Id, forceRefresh, cancellationToken).ConfigureAwait(false);
                if (quote.FromCache)
                {
                    cachedCount++;
                }
                else
                {
                    updatedCount++;
                }
            }
            catch (HttpRequestException ex)
            {
                failedCount++;
                errors.Add($"{security.Symbol}: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                failedCount++;
                errors.Add($"{security.Symbol}: {ex.Message}");
            }
            catch (JsonException ex)
            {
                failedCount++;
                errors.Add($"{security.Symbol}: {ex.Message}");
            }
        }

        return new MarketDataRefreshResultDto(updatedCount, cachedCount, failedCount, errors);
    }

    public async Task<IReadOnlyList<ExternalDividendEventDto>> GetDividendHistoryAsync(
        long securityId,
        DateOnly fromDate,
        DateOnly toDate,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(securityId);
        if (toDate < fromDate)
        {
            throw new ArgumentOutOfRangeException(nameof(toDate), "End date must be on or after start date.");
        }

        var settings = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        var provider = ResolveProvider(settings.Provider);

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var symbol = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                "SELECT Symbol FROM Securities WHERE Id = @SecurityId;",
                new { SecurityId = securityId },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (symbol is null)
        {
            throw new KeyNotFoundException($"Security {securityId} was not found.");
        }

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var nowUtc = _timeProvider.GetUtcNow();

        if (!forceRefresh)
        {
            var latestFetchedText = await connection.QuerySingleOrDefaultAsync<string?>(
                new CommandDefinition(
                    """
                    SELECT MAX(FetchedUtc)
                    FROM MarketDividendCache
                    WHERE Symbol = @Symbol AND ProviderName = @ProviderName;
                    """,
                    new { Symbol = normalizedSymbol, ProviderName = provider.DisplayName },
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(latestFetchedText) &&
                DateTimeOffset.TryParse(latestFetchedText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var latestFetchedUtc) &&
                (nowUtc - latestFetchedUtc) < TimeSpan.FromMinutes(settings.CacheTtlMinutes))
            {
                var cachedRows = await connection.QueryAsync<CachedDividendRow>(
                    new CommandDefinition(
                        """
                        SELECT Symbol, ExDividendDate, PaymentDate, AmountPerShare, ProviderName
                        FROM MarketDividendCache
                        WHERE Symbol = @Symbol
                          AND ProviderName = @ProviderName
                          AND PaymentDate >= @FromDate
                          AND PaymentDate <= @ToDate
                        ORDER BY PaymentDate ASC;
                        """,
                        new
                        {
                            Symbol = normalizedSymbol,
                            ProviderName = provider.DisplayName,
                            FromDate = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            ToDate = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        },
                        cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                return cachedRows
                    .Select(r => new ExternalDividendEventDto(
                        r.Symbol,
                        DateOnly.ParseExact(r.ExDividendDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        DateOnly.ParseExact(r.PaymentDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        r.AmountPerShare,
                        r.ProviderName))
                    .ToList();
            }
        }

        var fetched = await provider
            .GetDividendHistoryAsync(normalizedSymbol, fromDate, toDate, settings.ApiKey, cancellationToken)
            .ConfigureAwait(false);

        var fetchedUtcText = nowUtc.ToString("O", CultureInfo.InvariantCulture);
        const string upsertCacheSql = """
            INSERT INTO MarketDividendCache (Symbol, ExDividendDate, PaymentDate, AmountPerShare, ProviderName, FetchedUtc)
            VALUES (@Symbol, @ExDividendDate, @PaymentDate, @AmountPerShare, @ProviderName, @FetchedUtc)
            ON CONFLICT(Symbol, PaymentDate, ProviderName) DO UPDATE SET
                ExDividendDate = excluded.ExDividendDate,
                AmountPerShare = excluded.AmountPerShare,
                FetchedUtc = excluded.FetchedUtc;
            """;

        foreach (var evt in fetched)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    upsertCacheSql,
                    new
                    {
                        Symbol = normalizedSymbol,
                        ExDividendDate = evt.ExDividendDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        PaymentDate = evt.PaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        evt.AmountPerShare,
                        ProviderName = provider.DisplayName,
                        FetchedUtc = fetchedUtcText,
                    },
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        return fetched
            .Where(e => e.PaymentDate >= fromDate && e.PaymentDate <= toDate)
            .OrderBy(e => e.PaymentDate)
            .ToList();
    }

    public async Task<DividendImportResultDto> ImportDividendsForHoldingAsync(
        long holdingId,
        DateOnly fromDate,
        DateOnly toDate,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(holdingId);
        if (toDate < fromDate)
        {
            throw new ArgumentOutOfRangeException(nameof(toDate), "End date must be on or after start date.");
        }

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var securityId = await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(
                "SELECT SecurityId FROM Holdings WHERE Id = @HoldingId;",
                new { HoldingId = holdingId },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (!securityId.HasValue)
        {
            throw new KeyNotFoundException($"Holding {holdingId} was not found.");
        }

        var buys = (await connection.QueryAsync<MarketBuyRow>(
            new CommandDefinition(
                """
                SELECT NumberOfShares, PurchaseDate
                FROM MarketBuys
                WHERE HoldingId = @HoldingId;
                """,
                new { HoldingId = holdingId },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false))
            .Select(b => (
                b.NumberOfShares,
                PurchaseDate: DateOnly.ParseExact(b.PurchaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToList();

        var existingDates = (await connection.QueryAsync<string>(
            new CommandDefinition(
                "SELECT PaymentDate FROM DividendPayments WHERE HoldingId = @HoldingId;",
                new { HoldingId = holdingId },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false))
            .Select(d => DateOnly.ParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToHashSet();

        var events = await GetDividendHistoryAsync(
            securityId.Value,
            fromDate,
            toDate,
            forceRefresh,
            cancellationToken).ConfigureAwait(false);

        var importedCount = 0;
        var skippedCount = 0;

        const string insertDividendSql = """
            INSERT INTO DividendPayments (HoldingId, PaymentDate, ExDividendDate, Amount, IsReinvested, Notes)
            VALUES (@HoldingId, @PaymentDate, @ExDividendDate, @Amount, 0, @Notes);
            """;

        foreach (var evt in events)
        {
            if (existingDates.Contains(evt.PaymentDate))
            {
                skippedCount++;
                continue;
            }

            var sharesHeld = buys
                .Where(b => b.PurchaseDate <= evt.ExDividendDate)
                .Sum(b => b.NumberOfShares);

            var amount = Math.Round(sharesHeld * evt.AmountPerShare, 2, MidpointRounding.AwayFromZero);
            if (sharesHeld <= 0m || amount <= 0m)
            {
                skippedCount++;
                continue;
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    insertDividendSql,
                    new
                    {
                        HoldingId = holdingId,
                        PaymentDate = evt.PaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ExDividendDate = evt.ExDividendDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Amount = amount,
                        Notes = $"Imported from {evt.ProviderName}",
                    },
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            existingDates.Add(evt.PaymentDate);
            importedCount++;
        }

        return new DividendImportResultDto(importedCount, skippedCount, Array.Empty<string>());
    }

    private IMarketDataProvider ResolveProvider(MarketDataProviderType providerType)
    {
        if (!_providers.TryGetValue(providerType, out var provider))
        {
            throw new InvalidOperationException($"Market data provider '{providerType}' is not registered.");
        }

        return provider;
    }

    private sealed record SettingRow(string Key, string Value);

    private sealed record SecurityQuoteRow(
        long Id,
        string Symbol,
        string Name,
        decimal LastPrice,
        string? LastPriceUpdatedUtc);

    private sealed record SecuritySummaryRow(long Id, string Symbol);

    private sealed record CachedDividendRow(
        string Symbol,
        string ExDividendDate,
        string PaymentDate,
        decimal AmountPerShare,
        string ProviderName);

    private sealed record MarketBuyRow(decimal NumberOfShares, string PurchaseDate);
}
