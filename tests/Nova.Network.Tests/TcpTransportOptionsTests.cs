using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Tests;

public sealed class TcpTransportOptionsTests
{
    [Fact]
    public void Validate_WithDefaultValues_DoesNotThrow()
    {
        var options = new TcpTransportOptions();

        options.Validate();
    }

    [Fact]
    public void Validate_WithZeroConnectionLimit_Throws()
    {
        var options = new TcpTransportOptions
        {
            MaxConcurrentConnections = 0,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
