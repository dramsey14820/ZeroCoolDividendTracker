namespace DividendTracker.Core.Features.Analytics;

public sealed record DashboardSummaryDto(
    decimal TotalMarketValue,
    decimal TotalCostBasis,
    decimal TotalUnrealizedGainLoss,
    decimal TotalUnrealizedGainLossPercent,
    decimal Trailing12MonthIncome,
    decimal ProjectedAnnualIncome,
    decimal ProjectedMonthlyIncome,
    decimal LifetimeDividendIncome,
    decimal PortfolioCurrentYieldPercent,
    decimal PortfolioYieldOnCostPercent,
    int HoldingCount);

public sealed record HoldingAnalyticsDto(
    long HoldingId,
    long PortfolioId,
    string PortfolioName,
    long AccountId,
    string AccountName,
    long SecurityId,
    string SecuritySymbol,
    string SecurityName,
    string SectorName,
    string IndustryName,
    string InvestmentTypeName,
    decimal LastPrice,
    decimal TotalShares,
    decimal TotalCostBasis,
    decimal MarketValue,
    decimal UnrealizedGainLoss,
    decimal UnrealizedGainLossPercent,
    decimal Trailing12MonthDividends,
    decimal ProjectedAnnualIncome,
    decimal ProjectedMonthlyIncome,
    decimal LifetimeDividends,
    decimal CurrentYieldPercent,
    decimal YieldOnCostPercent);

public sealed record AllocationItemDto(
    string Name,
    decimal MarketValue,
    decimal CostBasis,
    decimal AllocationPercent,
    decimal Trailing12MonthIncome,
    int HoldingCount);

public sealed record MonthlyIncomePointDto(
    int Year,
    int Month,
    string Label,
    decimal TotalAmount,
    decimal CashAmount,
    decimal ReinvestedAmount,
    int PayoutCount);

public sealed record DashboardAnalyticsDto(
    long? PortfolioId,
    DateOnly AsOfDate,
    DashboardSummaryDto Summary,
    IReadOnlyList<HoldingAnalyticsDto> Holdings,
    IReadOnlyList<AllocationItemDto> SectorAllocations,
    IReadOnlyList<AllocationItemDto> IndustryAllocations,
    IReadOnlyList<AllocationItemDto> InvestmentTypeAllocations,
    IReadOnlyList<MonthlyIncomePointDto> MonthlyIncomeHistory);

