using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using static PeerCast.Nova.App.Tests.PublicChannelTestFixture;

namespace PeerCast.Nova.App.Tests;

/// <summary>Real PCP peer, strictly loopback/port 0; never opens host stores.</summary>
internal sealed class RelayActivationTestUpstream : IAsyncDisposable
{
    private readonly PcpRelayFanout _fanout = new();
    private readonly PcpDownstreamRelayServer _server;
    private readonly Task _run;
    public ChannelId ChannelId { get; } = new(Guid.NewGuid());
    public GenesisChannelItem Channel { get; }
    public GenesisGuiNextNativeTabRow Row => new(Channel, Channel);
    public RelayActivationTestUpstream(CancellationToken token, bool wrongChannel = false)
    {
        _server = new(new(IPAddress.Loopback, 0), _fanout, ChannelId, Guid.NewGuid(), Transport);
        _run = _server.RunAsync(token);
        var sourceId = wrongChannel ? Guid.NewGuid() : ChannelId.Value;
        Channel = new("Managed PCP upstream", $"pcp://127.0.0.1:{_server.LocalEndPoint.Port}/channel/{sourceId:N}",
            ChannelId: sourceId.ToString("N"));
    }
    public async Task SendAsync(CancellationToken token, uint packetType = 1)
    {
        await UntilAsync(() => _fanout.ActiveChildCount > 0, token);
        await _fanout.ConsumeAsync(new PcpChannelPacket(ChannelId, new(Guid.NewGuid()),
            0, packetType, 100, 100, new byte[] { 1 }, null), token);
    }
    public Task WaitForIdleAsync(CancellationToken token) => UntilAsync(() => _fanout.ActiveChildCount == 0, token);
    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        await _run;
        await _fanout.DisposeAsync();
    }
}
