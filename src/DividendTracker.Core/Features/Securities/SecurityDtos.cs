namespace DividendTracker.Core.Features.Securities;

public enum LookupCategory
{
    InvestmentType = 0,
    IndustryType = 1,
    SectorType = 2,
}

public sealed record LookupItemDto(
    long Id,
    string Name);

public sealed record LookupCatalogDto(
    IReadOnlyList<LookupItemDto> InvestmentTypes,
    IReadOnlyList<LookupItemDto> IndustryTypes,
    IReadOnlyList<LookupItemDto> SectorTypes);

public sealed record CreateLookupRequest(
    LookupCategory Category,
    string Name);

public sealed record UpdateLookupRequest(
    LookupCategory Category,
    long Id,
    string Name);

public sealed record SecurityDto(
    long Id,
    string Symbol,
    string Name,
    long InvestmentTypeId,
    string InvestmentTypeName,
    long IndustryTypeId,
    string IndustryTypeName,
    long SectorTypeId,
    string SectorTypeName,
    decimal LastPrice,
    string? LastPriceUpdatedUtc);

public sealed record CreateSecurityRequest(
    string Symbol,
    string Name,
    long InvestmentTypeId,
    long IndustryTypeId,
    long SectorTypeId,
    decimal LastPrice = 0m);

public sealed record UpdateSecurityRequest(
    long Id,
    string Symbol,
    string Name,
    long InvestmentTypeId,
    long IndustryTypeId,
    long SectorTypeId,
    decimal LastPrice);

