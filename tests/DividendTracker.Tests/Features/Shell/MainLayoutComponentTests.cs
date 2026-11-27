using Bunit;
using DividendTracker.Shared.UI.Layout;
using FluentAssertions;
using Xunit;

namespace DividendTracker.Tests.Features.Shell;

public sealed class MainLayoutComponentTests : BunitContext
{
    private static readonly string[] ExpectedNavLinks =
    [
        "Dashboard",
        "Portfolios",
        "Holdings",
        "Dividends",
        "Settings",
    ];

    [Fact]
    public void NavMenu_WhenRendered_DisplaysPrimaryNavigationLinks()
    {
        // Arrange & Act
        var cut = Render<NavMenu>();

        // Assert
        var links = cut.FindAll("a");
        links.Should().HaveCount(5);
        links.Select(a => a.TextContent.Trim()).Should().BeEquivalentTo(ExpectedNavLinks);
    }

    [Fact]
    public void MainLayout_WhenRenderedWithBodyContent_DisplaysNavigationAndBody()
    {
        // Arrange & Act
        var cut = Render<MainLayout>(parameters => parameters.Add(p => p.Body, builder => builder.AddContent(0, "Welcome Body")));

        // Assert
        cut.Find("nav").Should().NotBeNull();
        cut.Find("main").TextContent.Should().Contain("Welcome Body");
    }
}

