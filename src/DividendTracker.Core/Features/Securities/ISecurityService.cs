namespace DividendTracker.Core.Features.Securities;

public interface ISecurityService
{
    Task<LookupCatalogDto> GetLookupsAsync(CancellationToken cancellationToken = default);
    Task<LookupItemDto> CreateLookupAsync(CreateLookupRequest request, CancellationToken cancellationToken = default);
    Task<LookupItemDto> UpdateLookupAsync(UpdateLookupRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteLookupAsync(LookupCategory category, long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SecurityDto>> GetSecuritiesAsync(CancellationToken cancellationToken = default);
    Task<SecurityDto> CreateSecurityAsync(CreateSecurityRequest request, CancellationToken cancellationToken = default);
    Task<SecurityDto> UpdateSecurityAsync(UpdateSecurityRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteSecurityAsync(long securityId, CancellationToken cancellationToken = default);
}

