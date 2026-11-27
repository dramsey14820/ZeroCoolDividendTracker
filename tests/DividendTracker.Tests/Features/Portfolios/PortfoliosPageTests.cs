using Bunit;
using DividendTracker.Core.Features.Portfolios;
using DividendTracker.Shared.UI.Pages;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace DividendTracker.Tests.Features.Portfolios;

public sealed class PortfoliosPageTests : BunitContext
{
    [Fact]
    public void PortfoliosPage_WhenRendered_DisplaysPortfoliosBadgesAndAccounts()
    {
        // Arrange
        var portfolioService = Substitute.For<IPortfolioService>();
        portfolioService.GetPortfoliosAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PortfolioDto>
            {
                new(
                    1,
                    "Core Income",
                    "Primary dividend portfolio",
                    false,
                    "2026-01-01T00:00:00Z",
                    new List<AccountDto>
                    {
                        new(10, 1, "Fidelity Roth", "Roth IRA", "2026-01-01T00:00:00Z")
                    })
            });
        Services.AddSingleton(portfolioService);

        // Act
        var cut = Render<Portfolios>();

        // Assert
        cut.Markup.Should().Contain("Core Income");
        cut.Markup.Should().Contain("Actual");
        cut.Markup.Should().Contain("Fidelity Roth");
    }

    [Fact]
    public void PortfoliosPage_WhenNewPortfolioSubmitted_CallsCreatePortfolioAsyncAndRefreshesList()
    {
        // Arrange
        var portfolioService = Substitute.For<IPortfolioService>();
        var initialPortfolios = new List<PortfolioDto>();
        var refreshedPortfolios = new List<PortfolioDto>
        {
            new(2, "Dividend Growth", "Long-term compounders", true, "2026-01-02T00:00:00Z", Array.Empty<AccountDto>())
        };

        portfolioService.GetPortfoliosAsync(Arg.Any<CancellationToken>())
            .Returns(initialPortfolios, refreshedPortfolios);
        portfolioService.CreatePortfolioAsync(Arg.Any<CreatePortfolioRequest>(), Arg.Any<CancellationToken>())
            .Returns(refreshedPortfolios[0]);
        Services.AddSingleton(portfolioService);

        var cut = Render<Portfolios>();

        // Act
        cut.Find("#portfolio-name").Change("Dividend Growth");
        cut.Find("#portfolio-description").Change("Long-term compounders");
        cut.Find("#portfolio-modelled").Change(true);
        cut.Find("form.portfolio-form").Submit();

        // Assert
        portfolioService.Received(1).CreatePortfolioAsync(
            Arg.Is<CreatePortfolioRequest>(r =>
                r.Name == "Dividend Growth" &&
                r.Description == "Long-term compounders" &&
                r.IsModelled),
            Arg.Any<CancellationToken>());
        cut.Markup.Should().Contain("Dividend Growth");
        cut.Markup.Should().Contain("Modelled");
    }
}

