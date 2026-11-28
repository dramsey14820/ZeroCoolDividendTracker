namespace DividendTracker.Core.Features.Dividends;

public sealed record DividendPaymentDto(
    long Id,
    long HoldingId,
    long PortfolioId,
    string PortfolioName,
    long AccountId,
    string AccountName,
    long SecurityId,
    string SecuritySymbol,
    string SecurityName,
    DateOnly PaymentDate,
    DateOnly? ExDividendDate,
    decimal Amount,
    bool IsReinvested,
    string Notes,
    long? DripMarketBuyId,
    decimal? DripShares,
    decimal? DripPricePerShare);

public sealed record RecordDividendRequest(
    long HoldingId,
    DateOnly PaymentDate,
    DateOnly? ExDividendDate,
    decimal Amount,
    bool IsReinvested,
    string Notes = "",
    decimal? DripShares = null,
    decimal? DripPricePerShare = null);

public sealed record UpdateDividendRequest(
    long Id,
    DateOnly PaymentDate,
    DateOnly? ExDividendDate,
    decimal Amount,
    bool IsReinvested,
    string Notes = "",
    decimal? DripShares = null,
    decimal? DripPricePerShare = null);

