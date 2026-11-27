namespace DividendTracker.Core.Features.Holdings;

public sealed record MarketBuyDto(
    long Id,
    long HoldingId,
    decimal NumberOfShares,
    decimal PricePerShare,
    DateOnly PurchaseDate,
    bool IsDrip,
    long? DividendPaymentId)
{
    public decimal TotalCost => NumberOfShares * PricePerShare;
}

public sealed record HoldingDto(
    long Id,
    long PortfolioId,
    string PortfolioName,
    long AccountId,
    string AccountName,
    long SecurityId,
    string SecuritySymbol,
    string SecurityName,
    decimal LastPrice,
    bool IsDripEnabled,
    decimal TotalShares,
    decimal TotalCostBasis,
    decimal AverageCostPerShare,
    decimal MarketValue,
    IReadOnlyList<MarketBuyDto> MarketBuys);

public sealed record CreateHoldingRequest(
    long PortfolioId,
    long AccountId,
    long SecurityId,
    bool IsDripEnabled);

public sealed record UpdateHoldingRequest(
    long Id,
    bool IsDripEnabled);

public sealed record CreateMarketBuyRequest(
    long HoldingId,
    decimal NumberOfShares,
    decimal PricePerShare,
    DateOnly PurchaseDate,
    bool IsDrip = false);

public sealed record UpdateMarketBuyRequest(
    long Id,
    decimal NumberOfShares,
    decimal PricePerShare,
    DateOnly PurchaseDate,
    bool IsDrip);

