using System.Globalization;
using Dapper;
using DividendTracker.Core.Infrastructure.Persistence;

namespace DividendTracker.Core.Features.Analytics;

public sealed class AnalyticsService : IAnalyticsService
{
    private const string HoldingsSql = """
        SELECT
            h.Id AS HoldingId,
            h.PortfolioId,
            p.Name AS PortfolioName,
            h.AccountId,
            a.Name AS AccountName,
            h.SecurityId,
            s.Symbol AS SecuritySymbol,
            s.Name AS SecurityName,
            st.Name AS SectorName,
            it.Name AS IndustryName,
            inv.Name AS InvestmentTypeName,
            s.LastPrice
        FROM Holdings h
        INNER JOIN Portfolios p ON p.Id = h.PortfolioId
        INNER JOIN Accounts a ON a.Id = h.AccountId
        INNER JOIN Securities s ON s.Id = h.SecurityId
        INNER JOIN SectorTypes st ON st.Id = s.SectorTypeId
        INNER JOIN IndustryTypes it ON it.Id = s.IndustryTypeId
        INNER JOIN InvestmentTypes inv ON inv.Id = s.InvestmentTypeId
        WHERE (@PortfolioId IS NULL OR h.PortfolioId = @PortfolioId);
        """;

    private const string MarketBuysSql = """
        SELECT
            mb.HoldingId,
            mb.NumberOfShares,
            mb.PricePerShare
        FROM MarketBuys mb
        INNER JOIN Holdings h ON h.Id = mb.HoldingId
        WHERE (@PortfolioId IS NULL OR h.PortfolioId = @PortfolioId);
        """;

    private const string DividendsSql = """
        SELECT
            dp.HoldingId,
            dp.PaymentDate,
            dp.Amount,
            dp.IsReinvested
        FROM DividendPayments dp
        INNER JOIN Holdings h ON h.Id = dp.HoldingId
        WHERE (@PortfolioId IS NULL OR h.PortfolioId = @PortfolioId);
        """;

    private readonly ISqliteConnectionFactory _connectionFactory;

    public AnalyticsService(ISqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task<DashboardAnalyticsDto> GetDashboardAsync(
        long? portfolioId = null,
        DateOnly? asOfDate = null,
        CancellationToken cancellationToken = default)
    {
        if (portfolioId.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(portfolioId.Value);
        }

        var effectiveAsOf = asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var ttmStartDate = effectiveAsOf.AddYears(-1).AddDays(1);
        var parameters = new { PortfolioId = portfolioId };

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var holdingRows = (await connection.QueryAsync<HoldingRow>(
            new CommandDefinition(HoldingsSql, parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false)).AsList();

        var buyRows = (await connection.QueryAsync<MarketBuyRow>(
            new CommandDefinition(MarketBuysSql, parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false)).AsList();

        var dividendRows = (await connection.QueryAsync<DividendRow>(
            new CommandDefinition(DividendsSql, parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false)).AsList();

        var buysByHolding = buyRows
            .GroupBy(b => b.HoldingId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var parsedDividends = dividendRows
            .Select(d => new ParsedDividend(
                d.HoldingId,
                DateOnly.ParseExact(d.PaymentDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                d.Amount,
                d.IsReinvested))
            .Where(d => d.PaymentDate <= effectiveAsOf)
            .ToList();

        var dividendsByHolding = parsedDividends
            .GroupBy(d => d.HoldingId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var holdingAnalytics = holdingRows
            .Select(row => BuildHoldingAnalytics(
                row,
                buysByHolding.TryGetValue(row.HoldingId, out var holdingBuys) ? holdingBuys : [],
                dividendsByHolding.TryGetValue(row.HoldingId, out var holdingDivs) ? holdingDivs : [],
                ttmStartDate))
            .OrderByDescending(h => h.MarketValue)
            .ThenBy(h => h.SecuritySymbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.HoldingId)
            .ToList();

        var totalMarketValue = holdingAnalytics.Sum(h => h.MarketValue);
        var totalCostBasis = holdingAnalytics.Sum(h => h.TotalCostBasis);
        var totalUnrealizedGainLoss = totalMarketValue - totalCostBasis;
        var totalTtmIncome = holdingAnalytics.Sum(h => h.Trailing12MonthDividends);
        var totalLifetimeIncome = holdingAnalytics.Sum(h => h.LifetimeDividends);

        var summary = new DashboardSummaryDto(
            TotalMarketValue: totalMarketValue,
            TotalCostBasis: totalCostBasis,
            TotalUnrealizedGainLoss: totalUnrealizedGainLoss,
            TotalUnrealizedGainLossPercent: CalculatePercent(totalUnrealizedGainLoss, totalCostBasis),
            Trailing12MonthIncome: totalTtmIncome,
            ProjectedAnnualIncome: totalTtmIncome,
            ProjectedMonthlyIncome: Round2(totalTtmIncome / 12m),
            LifetimeDividendIncome: totalLifetimeIncome,
            PortfolioCurrentYieldPercent: CalculatePercent(totalTtmIncome, totalMarketValue),
            PortfolioYieldOnCostPercent: CalculatePercent(totalTtmIncome, totalCostBasis),
            HoldingCount: holdingAnalytics.Count);

        var sectorAllocations = BuildAllocations(holdingAnalytics, h => h.SectorName, totalMarketValue);
        var industryAllocations = BuildAllocations(holdingAnalytics, h => h.IndustryName, totalMarketValue);
        var investmentTypeAllocations = BuildAllocations(holdingAnalytics, h => h.InvestmentTypeName, totalMarketValue);

        var monthlyIncomeHistory = parsedDividends
            .GroupBy(d => (d.PaymentDate.Year, d.PaymentDate.Month))
            .OrderBy(g => g.Key.Year)
            .ThenBy(g => g.Key.Month)
            .Select(g =>
            {
                var reinvested = g.Where(d => d.IsReinvested).Sum(d => d.Amount);
                var cash = g.Where(d => !d.IsReinvested).Sum(d => d.Amount);
                var label = string.Create(CultureInfo.InvariantCulture, $"{g.Key.Year:D4}-{g.Key.Month:D2}");
                return new MonthlyIncomePointDto(
                    Year: g.Key.Year,
                    Month: g.Key.Month,
                    Label: label,
                    TotalAmount: cash + reinvested,
                    CashAmount: cash,
                    ReinvestedAmount: reinvested,
                    PayoutCount: g.Count());
            })
            .ToList();

        return new DashboardAnalyticsDto(
            PortfolioId: portfolioId,
            AsOfDate: effectiveAsOf,
            Summary: summary,
            Holdings: holdingAnalytics,
            SectorAllocations: sectorAllocations,
            IndustryAllocations: industryAllocations,
            InvestmentTypeAllocations: investmentTypeAllocations,
            MonthlyIncomeHistory: monthlyIncomeHistory);
    }

    private static HoldingAnalyticsDto BuildHoldingAnalytics(
        HoldingRow row,
        IReadOnlyList<MarketBuyRow> buys,
        IReadOnlyList<ParsedDividend> dividends,
        DateOnly ttmStartDate)
    {
        var totalShares = buys.Sum(b => b.NumberOfShares);
        var totalCostBasis = buys.Sum(b => b.NumberOfShares * b.PricePerShare);
        var marketValue = totalShares * row.LastPrice;
        var unrealizedGainLoss = marketValue - totalCostBasis;

        var lifetimeDividends = dividends.Sum(d => d.Amount);
        var ttmDividends = dividends
            .Where(d => d.PaymentDate >= ttmStartDate)
            .Sum(d => d.Amount);

        return new HoldingAnalyticsDto(
            HoldingId: row.HoldingId,
            PortfolioId: row.PortfolioId,
            PortfolioName: row.PortfolioName,
            AccountId: row.AccountId,
            AccountName: row.AccountName,
            SecurityId: row.SecurityId,
            SecuritySymbol: row.SecuritySymbol,
            SecurityName: row.SecurityName,
            SectorName: row.SectorName,
            IndustryName: row.IndustryName,
            InvestmentTypeName: row.InvestmentTypeName,
            LastPrice: row.LastPrice,
            TotalShares: totalShares,
            TotalCostBasis: totalCostBasis,
            MarketValue: marketValue,
            UnrealizedGainLoss: unrealizedGainLoss,
            UnrealizedGainLossPercent: CalculatePercent(unrealizedGainLoss, totalCostBasis),
            Trailing12MonthDividends: ttmDividends,
            ProjectedAnnualIncome: ttmDividends,
            ProjectedMonthlyIncome: Round2(ttmDividends / 12m),
            LifetimeDividends: lifetimeDividends,
            CurrentYieldPercent: CalculatePercent(ttmDividends, marketValue),
            YieldOnCostPercent: CalculatePercent(ttmDividends, totalCostBasis));
    }

    private static List<AllocationItemDto> BuildAllocations(
        IReadOnlyList<HoldingAnalyticsDto> holdings,
        Func<HoldingAnalyticsDto, string> keySelector,
        decimal totalMarketValue)
    {
        return holdings
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var marketValue = g.Sum(h => h.MarketValue);
                var costBasis = g.Sum(h => h.TotalCostBasis);
                var ttmIncome = g.Sum(h => h.Trailing12MonthDividends);
                return new AllocationItemDto(
                    Name: g.First() is var first ? keySelector(first) : g.Key,
                    MarketValue: marketValue,
                    CostBasis: costBasis,
                    AllocationPercent: CalculatePercent(marketValue, totalMarketValue),
                    Trailing12MonthIncome: ttmIncome,
                    HoldingCount: g.Count());
            })
            .OrderByDescending(a => a.MarketValue)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static decimal CalculatePercent(decimal numerator, decimal denominator)
    {
        return denominator > 0m
            ? Round2((numerator / denominator) * 100m)
            : 0m;
    }

    private static decimal Round2(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private sealed class HoldingRow
    {
        public long HoldingId { get; init; }
        public long PortfolioId { get; init; }
        public string PortfolioName { get; init; } = string.Empty;
        public long AccountId { get; init; }
        public string AccountName { get; init; } = string.Empty;
        public long SecurityId { get; init; }
        public string SecuritySymbol { get; init; } = string.Empty;
        public string SecurityName { get; init; } = string.Empty;
        public string SectorName { get; init; } = string.Empty;
        public string IndustryName { get; init; } = string.Empty;
        public string InvestmentTypeName { get; init; } = string.Empty;
        public decimal LastPrice { get; init; }
    }

    private sealed class MarketBuyRow
    {
        public long HoldingId { get; init; }
        public decimal NumberOfShares { get; init; }
        public decimal PricePerShare { get; init; }
    }

    private sealed class DividendRow
    {
        public long HoldingId { get; init; }
        public string PaymentDate { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public bool IsReinvested { get; init; }
    }

    private sealed record ParsedDividend(
        long HoldingId,
        DateOnly PaymentDate,
        decimal Amount,
        bool IsReinvested);
}

