using DividendTracker.Core.Features.Securities;
using DividendTracker.Core.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DividendTracker.Tests.Features.Securities;

public sealed class SecurityServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"zcdt_security_tests_{Guid.NewGuid():N}");

    private async Task<SecurityService> CreateInitializedServiceAsync()
    {
        var dbPath = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(new SqliteOptions($"Data Source={dbPath}"));
        var initializer = new SqliteDatabaseInitializer(factory);
        await initializer.InitializeAsync();
        return new SecurityService(factory, TimeProvider.System);
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
    public async Task CreateListAndUpdateSecurityAsync_NormalizesSymbolJoinsTaxonomiesAndPersistsChanges()
    {
        // Arrange
        var service = await CreateInitializedServiceAsync();
        var lookups = await service.GetLookupsAsync();
        var etf = lookups.InvestmentTypes.Single(x => x.Name == "ETF");
        var general = lookups.IndustryTypes.Single(x => x.Name == "General");
        var financials = lookups.SectorTypes.Single(x => x.Name == "Financials");

        // Act - Create
        var created = await service.CreateSecurityAsync(new CreateSecurityRequest(
            " schd ",
            "Schwab US Dividend Equity ETF",
            etf.Id,
            general.Id,
            financials.Id,
            82.45m));

        var afterCreate = await service.GetSecuritiesAsync();

        // Assert - Create
        created.Symbol.Should().Be("SCHD");
        created.LastPriceUpdatedUtc.Should().NotBeNullOrWhiteSpace();
        afterCreate.Should().ContainSingle(s =>
            s.Id == created.Id &&
            s.Symbol == "SCHD" &&
            s.Name == "Schwab US Dividend Equity ETF" &&
            s.InvestmentTypeName == "ETF" &&
            s.IndustryTypeName == "General" &&
            s.SectorTypeName == "Financials" &&
            s.LastPrice == 82.45m);

        // Act - Update
        var updated = await service.UpdateSecurityAsync(new UpdateSecurityRequest(
            created.Id,
            "schd",
            "Schwab Dividend ETF",
            etf.Id,
            general.Id,
            financials.Id,
            84.10m));

        var afterUpdate = await service.GetSecuritiesAsync();

        // Assert - Update
        updated.Name.Should().Be("Schwab Dividend ETF");
        updated.LastPrice.Should().Be(84.10m);
        afterUpdate.Should().ContainSingle(s =>
            s.Id == created.Id &&
            s.Name == "Schwab Dividend ETF" &&
            s.LastPrice == 84.10m);
    }

    [Fact]
    public async Task CreateSecurityAsync_WithDuplicateSymbolOrNegativePrice_ThrowsExpectedException()
    {
        // Arrange
        var service = await CreateInitializedServiceAsync();
        var lookups = await service.GetLookupsAsync();
        var reit = lookups.InvestmentTypes.Single(x => x.Name == "REIT");
        var general = lookups.IndustryTypes.Single(x => x.Name == "General");
        var realEstate = lookups.SectorTypes.Single(x => x.Name == "Real Estate");

        await service.CreateSecurityAsync(new CreateSecurityRequest(
            "O",
            "Realty Income Corp",
            reit.Id,
            general.Id,
            realEstate.Id,
            58.25m));

        // Act
        var duplicateAct = async () => await service.CreateSecurityAsync(new CreateSecurityRequest(
            "o",
            "Duplicate Realty Income",
            reit.Id,
            general.Id,
            realEstate.Id,
            58.25m));

        var negativePriceAct = async () => await service.CreateSecurityAsync(new CreateSecurityRequest(
            "MSFT",
            "Microsoft Corporation",
            reit.Id,
            general.Id,
            realEstate.Id,
            -1.00m));

        // Assert
        await duplicateAct.Should().ThrowAsync<InvalidOperationException>();
        await negativePriceAct.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task LookupTaxonomyCrud_EnforcesUniquenessAndDeleteRestrictionWhenReferenced()
    {
        // Arrange
        var service = await CreateInitializedServiceAsync();
        var lookups = await service.GetLookupsAsync();
        var reit = lookups.InvestmentTypes.Single(x => x.Name == "REIT");
        var realEstate = lookups.SectorTypes.Single(x => x.Name == "Real Estate");

        // Act - Create lookup
        var retailReit = await service.CreateLookupAsync(
            new CreateLookupRequest(LookupCategory.IndustryType, "Retail REIT"));

        var afterAdd = await service.GetLookupsAsync();
        afterAdd.IndustryTypes.Should().Contain(x => x.Id == retailReit.Id && x.Name == "Retail REIT");

        // Duplicate lookup rejected
        var duplicateLookupAct = async () => await service.CreateLookupAsync(
            new CreateLookupRequest(LookupCategory.IndustryType, "Retail REIT"));
        await duplicateLookupAct.Should().ThrowAsync<InvalidOperationException>();

        // Reference lookup from a security
        await service.CreateSecurityAsync(new CreateSecurityRequest(
            "O",
            "Realty Income Corp",
            reit.Id,
            retailReit.Id,
            realEstate.Id,
            58.00m));

        // Act - Delete referenced lookup
        var deleteReferencedAct = async () => await service.DeleteLookupAsync(
            LookupCategory.IndustryType,
            retailReit.Id);

        // Assert
        await deleteReferencedAct.Should().ThrowAsync<InvalidOperationException>();
        var afterFailedDelete = await service.GetLookupsAsync();
        afterFailedDelete.IndustryTypes.Should().Contain(x => x.Id == retailReit.Id);
    }

    [Fact]
    public async Task CreateOrUpdateSecurityAsync_WithMissingTaxonomyOrSecurityId_ThrowsKeyNotFoundException()
    {
        // Arrange
        var service = await CreateInitializedServiceAsync();
        var lookups = await service.GetLookupsAsync();
        var stock = lookups.InvestmentTypes.Single(x => x.Name == "Stock");
        var general = lookups.IndustryTypes.Single(x => x.Name == "General");
        var tech = lookups.SectorTypes.Single(x => x.Name == "Technology");

        // Act
        var missingSectorAct = async () => await service.CreateSecurityAsync(new CreateSecurityRequest(
            "AAPL",
            "Apple Inc.",
            stock.Id,
            general.Id,
            9999,
            225.00m));

        var missingSecurityAct = async () => await service.UpdateSecurityAsync(new UpdateSecurityRequest(
            9999,
            "AAPL",
            "Apple Inc.",
            stock.Id,
            general.Id,
            tech.Id,
            225.00m));

        // Assert
        await missingSectorAct.Should().ThrowAsync<KeyNotFoundException>();
        await missingSecurityAct.Should().ThrowAsync<KeyNotFoundException>();
    }
}

