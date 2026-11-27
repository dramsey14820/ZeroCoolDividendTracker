using Bunit;
using DividendTracker.Core.Common;
using DividendTracker.Shared.UI.Pages;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace DividendTracker.Tests.Features.Shell;

public sealed class HomeComponentTests : BunitContext
{
    [Fact]
    public void Home_WhenRendered_DisplaysApplicationMetadataFromIAppInfoService()
    {
        // Arrange
        var appInfoService = Substitute.For<IAppInfoService>();
        appInfoService.GetAppInfo().Returns(new AppInfoDto("ZeroCool Dividend Tracker", "Blazor Server", "1.0.0"));
        Services.AddSingleton(appInfoService);

        // Act
        var cut = Render<Home>();

        // Assert
        cut.Markup.Should().Contain("ZeroCool Dividend Tracker");
        cut.Markup.Should().Contain("Blazor Server");
        cut.Markup.Should().Contain("1.0.0");
    }
}

