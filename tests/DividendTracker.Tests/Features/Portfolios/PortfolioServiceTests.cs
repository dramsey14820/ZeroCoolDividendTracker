using DividendTracker.Core.Features.Portfolios;
using DividendTracker.Core.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DividendTracker.Tests.Features.Portfolios;

public sealed class PortfolioServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"zcdt_portfolio_tests_{Guid.NewGuid():N}");

    private async Task<PortfolioService> CreateInitializedServiceAsync()
    {
        var dbPath = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(new SqliteOptions($"Data Source={dbPath}"));
        var initializer = new SqliteDatabaseInitializer(factory);
        await initializer.InitializeAsync();
        return new PortfolioService(factory, TimeProvider.System);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAndGetPortfoliosAsync_ReturnsActualAndModelledPortfoliosWithAccountsOrderedByName()
    {
        // Arrange
        var service = await CreateInitializedServiceAsync();

        // Act
        var watchlist = await service.CreatePortfolioAsync(
            new CreatePortfolioRequest("Watchlist", "Modelled ideas", true));
        var coreIncome = await service.CreatePortfolioAsync(
            new CreatePortfolioRequest("Core Income", "Primary dividend portfolio", false));
        var account = await service.CreateAccountAsync(
            new CreateAccountRequest(coreIncome.Id, "Fidelity Roth", "Roth IRA"));

        var portfolios = await service.GetPortfoliosAsync();

        // Assert
        portfolios.Should().HaveCount(2);
        portfolios[0].Name.Should().Be("Core Income");
        portfolios[0].IsModelled.Should().BeFalse();
        portfolios[0].Accounts.Should().ContainSingle(a =>
            a.Id == account.Id &&
            a.Name == "Fidelity Roth" &&
            a.AccountType == "Roth IRA");

        portfolios[1].Id.Should().Be(watchlist.Id);
        portfolios[1].Name.Should().Be("Watchlist");
        portfolios[1].IsModelled.Should().BeTrue();
        portfolios[1].Accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdatePortfolioAndAccountAsync_UpdatesPersistedDetails()
    {
        // Arrange
        var service = await CreateInitializedServiceAsync();
        var portfolio = await service.CreatePortfolioAsync(
            new CreatePortfolioRequest("Core Income", "Initial description", false));
        var account = await service.CreateAccountAsync(
            new CreateAccountRequest(portfolio.Id, "Taxable Brokerage", "Taxable"));

        // Act
        var updatedPortfolio = await service.UpdatePortfolioAsync(
            new UpdatePortfolioRequest(portfolio.Id, "Dividend Growth", "Updated description", true));
        var updatedAccount = await service.UpdateAccountAsync(
            new UpdateAccountRequest(account.Id, "Schwab Taxable", "Taxable"));

        var portfolios = await service.GetPortfoliosAsync();

        // Assert
        updatedPortfolio.Name.Should().Be("Dividend Growth");
        updatedPortfolio.IsModelled.Should().BeTrue();
        updatedAccount.Name.Should().Be("Schwab Taxable");

        portfolios.Should().ContainSingle();
        portfolios[0].Name.Should().Be("Dividend Growth");
        portfolios[0].Description.Should().Be("Updated description");
        portfolios[0].IsModelled.Should().BeTrue();
        portfolios[0].Accounts.Should().ContainSingle(a =>
            a.Id == account.Id &&
            a.Name == "Schwab Taxable" &&
            a.AccountType == "Taxable");
    }

    [Fact]
    public async Task DeletePortfolioAsync_DeletesPortfolioAndCascadesChildAccounts()
    {
        // Arrange
        var service = await CreateInitializedServiceAsync();
        var portfolio = await service.CreatePortfolioAsync(
            new CreatePortfolioRequest("Core Income", null, false));
        await service.CreateAccountAsync(new CreateAccountRequest(portfolio.Id, "Fidelity Roth", "Roth IRA"));
        await service.CreateAccountAsync(new CreateAccountRequest(portfolio.Id, "Schwab Taxable", "Taxable"));

        // Act
        var deleted = await service.DeletePortfolioAsync(portfolio.Id);
        var portfolios = await service.GetPortfoliosAsync();

        // Assert
        deleted.Should().BeTrue();
        portfolios.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreatePortfolioAsync_WithBlankName_ThrowsArgumentExceptionAndInsertsNothing(string blankName)
    {
        // Arrange
        var service = await CreateInitializedServiceAsync();

        // Act
        var act = async () => await service.CreatePortfolioAsync(
            new CreatePortfolioRequest(blankName, "Desc", false));

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
        (await service.GetPortfoliosAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("   ", "Taxable")]
    [InlineData("Fidelity Roth", "   ")]
    public async Task CreateAccountAsync_WithBlankNameOrType_ThrowsArgumentExceptionAndInsertsNothing(
        string accountName,
        string accountType)
    {
        // Arrange
        var service = await CreateInitializedServiceAsync();
        var portfolio = await service.CreatePortfolioAsync(
            new CreatePortfolioRequest("Core Income", null, false));

        // Act
        var act = async () => await service.CreateAccountAsync(
            new CreateAccountRequest(portfolio.Id, accountName, accountType));

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
        var portfolios = await service.GetPortfoliosAsync();
        portfolios.Should().ContainSingle();
        portfolios[0].Accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAccountAsync_ForMissingPortfolio_ThrowsKeyNotFoundException()
    {
        // Arrange
        var service = await CreateInitializedServiceAsync();

        // Act
        var act = async () => await service.CreateAccountAsync(
            new CreateAccountRequest(999, "Orphan Account", "Taxable"));

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}

