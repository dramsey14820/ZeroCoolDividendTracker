namespace DividendTracker.Core.Features.Portfolios;

public sealed record AccountDto(
    long Id,
    long PortfolioId,
    string Name,
    string AccountType,
    string CreatedUtc);

public sealed record PortfolioDto(
    long Id,
    string Name,
    string Description,
    bool IsModelled,
    string CreatedUtc,
    IReadOnlyList<AccountDto> Accounts);

public sealed record CreatePortfolioRequest(
    string Name,
    string? Description,
    bool IsModelled);

public sealed record UpdatePortfolioRequest(
    long Id,
    string Name,
    string? Description,
    bool IsModelled);

public sealed record CreateAccountRequest(
    long PortfolioId,
    string Name,
    string AccountType);

public sealed record UpdateAccountRequest(
    long Id,
    string Name,
    string AccountType);

