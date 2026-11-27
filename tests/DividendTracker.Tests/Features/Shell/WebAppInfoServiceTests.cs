using DividendTracker.Web.Services;
using FluentAssertions;
using Xunit;

namespace DividendTracker.Tests.Features.Shell;

public sealed class WebAppInfoServiceTests
{
    [Fact]
    public void GetAppInfo_WhenCalled_ReturnsBlazorServerMetadata()
    {
        // Arrange
        var service = new WebAppInfoService();

        // Act
        var appInfo = service.GetAppInfo();

        // Assert
        appInfo.ApplicationName.Should().Be("ZeroCool Dividend Tracker");
        appInfo.PlatformName.Should().Be("Blazor Server");
        appInfo.Version.Should().NotBeNullOrWhiteSpace();
    }
}

