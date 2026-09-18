using Microsoft.Extensions.Hosting;
using Moq;
using MuktoAin.Web.Services;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class DevProcessWatchdogTests
{
    [Fact]
    public void Initialize_WhenNotDevelopment_DoesNotThrowOrMonitor()
    {
        // Arrange
        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns(Environments.Production);

        // Act & Assert (should safely return without doing any dev monitoring)
        var exception = Record.Exception(() => DevProcessWatchdog.Initialize(mockEnv.Object));
        Assert.Null(exception);
    }
}
