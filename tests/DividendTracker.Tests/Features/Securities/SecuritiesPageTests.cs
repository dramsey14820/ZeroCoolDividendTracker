using Bunit;
using DividendTracker.Core.Features.Securities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using SecuritiesPage = DividendTracker.Shared.UI.Pages.Securities;

namespace DividendTracker.Tests.Features.Securities;

public sealed class SecuritiesPageTests : BunitContext
{
    private static LookupCatalogDto CreateDefaultLookups() =>
        new(
            new List<LookupItemDto> { new(1, "ETF"), new(2, "Stock") },
            new List<LookupItemDto> { new(1, "General") },
            new List<LookupItemDto> { new(1, "Financials"), new(2, "Technology") });

    [Fact]
    public void SecuritiesPage_WhenRendered_DisplaysSecuritiesTaxonomiesAndLookupLists()
    {
        // Arrange
        var securityService = Substitute.For<ISecurityService>();
        securityService.GetLookupsAsync(Arg.Any<CancellationToken>())
            .Returns(CreateDefaultLookups());
        securityService.GetSecuritiesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SecurityDto>
            {
                new(
                    1,
                    "SCHD",
                    "Schwab US Dividend Equity ETF",
                    1,
                    "ETF",
                    1,
                    "General",
                    1,
                    "Financials",
                    82.45m,
                    "2026-01-01T00:00:00Z")
            });
        Services.AddSingleton(securityService);

        // Act
        var cut = Render<SecuritiesPage>();

        // Assert
        cut.Markup.Should().Contain("SCHD");
        cut.Markup.Should().Contain("Schwab US Dividend Equity ETF");
        cut.Markup.Should().Contain("ETF");
        cut.Markup.Should().Contain("Financials");
        cut.Markup.Should().Contain("General");
    }

    [Fact]
    public void SecuritiesPage_WhenNewSecuritySubmitted_CallsCreateSecurityAsyncAndRefreshesList()
    {
        // Arrange
        var securityService = Substitute.For<ISecurityService>();
        var initialSecurities = new List<SecurityDto>();
        var refreshedSecurities = new List<SecurityDto>
        {
            new(
                10,
                "MSFT",
                "Microsoft Corporation",
                2,
                "Stock",
                1,
                "General",
                2,
                "Technology",
                420.50m,
                "2026-01-02T00:00:00Z")
        };

        securityService.GetLookupsAsync(Arg.Any<CancellationToken>())
            .Returns(CreateDefaultLookups());
        securityService.GetSecuritiesAsync(Arg.Any<CancellationToken>())
            .Returns(initialSecurities, refreshedSecurities);
        securityService.CreateSecurityAsync(Arg.Any<CreateSecurityRequest>(), Arg.Any<CancellationToken>())
            .Returns(refreshedSecurities[0]);
        Services.AddSingleton(securityService);

        var cut = Render<SecuritiesPage>();

        // Act
        cut.Find("#security-symbol").Change("MSFT");
        cut.Find("#security-name").Change("Microsoft Corporation");
        cut.Find("#security-investment-type").Change("2");
        cut.Find("#security-industry-type").Change("1");
        cut.Find("#security-sector-type").Change("2");
        cut.Find("#security-last-price").Change("420.50");
        cut.Find("form.security-form").Submit();

        // Assert
        securityService.Received(1).CreateSecurityAsync(
            Arg.Is<CreateSecurityRequest>(r =>
                r.Symbol == "MSFT" &&
                r.Name == "Microsoft Corporation" &&
                r.InvestmentTypeId == 2 &&
                r.IndustryTypeId == 1 &&
                r.SectorTypeId == 2 &&
                r.LastPrice == 420.50m),
            Arg.Any<CancellationToken>());
        cut.Markup.Should().Contain("MSFT");
        cut.Markup.Should().Contain("Microsoft Corporation");
    }
}

