namespace DividendTracker.Core.Features.Holdings;

public interface IHoldingService
{
    Task<IReadOnlyList<HoldingDto>> GetHoldingsAsync(long? portfolioId = null, CancellationToken cancellationToken = default);
    Task<HoldingDto> CreateHoldingAsync(CreateHoldingRequest request, CancellationToken cancellationToken = default);
    Task<HoldingDto> UpdateHoldingAsync(UpdateHoldingRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteHoldingAsync(long holdingId, CancellationToken cancellationToken = default);

    Task<MarketBuyDto> CreateMarketBuyAsync(CreateMarketBuyRequest request, CancellationToken cancellationToken = default);
    Task<MarketBuyDto> UpdateMarketBuyAsync(UpdateMarketBuyRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteMarketBuyAsync(long marketBuyId, CancellationToken cancellationToken = default);
}

