namespace DividendTracker.Core.Features.Portfolios;

public interface IPortfolioService
{
    Task<IReadOnlyList<PortfolioDto>> GetPortfoliosAsync(CancellationToken cancellationToken = default);
    Task<PortfolioDto> CreatePortfolioAsync(CreatePortfolioRequest request, CancellationToken cancellationToken = default);
    Task<PortfolioDto> UpdatePortfolioAsync(UpdatePortfolioRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeletePortfolioAsync(long portfolioId, CancellationToken cancellationToken = default);
    Task<AccountDto> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default);
    Task<AccountDto> UpdateAccountAsync(UpdateAccountRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAccountAsync(long accountId, CancellationToken cancellationToken = default);
}

