using PeerCast.Nova.Foundation.Logging;

namespace PeerCast.Nova.Foundation.Tests;

public sealed class ConsoleNovaLoggerTests
{
    [Fact]
    public void Log_WithBlankMessage_ThrowsArgumentException()
    {
        var logger = new ConsoleNovaLogger();

        Assert.Throws<ArgumentException>(
            () => logger.Log(LogLevel.Information, " "));
    }
}
