using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpChannelSessionRedirectTests
{
    [Fact]
    public async Task Referrals_StartupRejectsKnownForeignChannelAndOwnSidWithoutChangingLegacyHosts()
    {
        await using var transport = new FakeTransport();
        var goodEndpoint = Endpoint("9.9.9.9", 7146);
        var listener = transport.AddRedirect(goodEndpoint);
        using var deadline = new CancellationTokenSource(TestTimeout);
        await using var session = CreateSession(transport, CreateTracker(), new CollectingPacketConsumer(), 0);
        var wrongChannel = PcpAtom.Parent(PcpKnownAtomNames.Host,
            CreateHost(("1.1.1.1", 7144)).Children.Concat(new[]
            {
                PcpAtom.Leaf(PcpAtomName.FromAscii("cid"), PcpWireValueCodec.EncodeGuid(Guid.NewGuid())),
            }).ToArray());
        var own = PcpAtom.Parent(PcpKnownAtomNames.Host,
            CreateHost(("8.8.8.8", 7145)).Children.Concat(new[]
            {
                PcpAtom.Leaf(PcpAtomName.FromAscii("id"), PcpWireValueCodec.EncodeGuid(SessionId.Value)),
            }).ToArray());
        var original = RunRelayFullPeerAsync(transport.OriginalListener, "original.example:7144", 0,
            [wrongChannel, own, CreateHost(("9.9.9.9", 7146))], deadline.Token);
        var accepted = RunAcceptedPeerAsync(listener, "9.9.9.9:7146", 0, [CreateQuitAtom()], deadline.Token);
        var result = await session.RunAsync("original.example", 7144, deadline.Token);
        await original;
        await accepted;
        Assert.Equal(PcpChannelSessionEndReason.QuitReceived, result.EndReason);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(new[] { goodEndpoint }, transport.LogicalRedirectEndpoints);
    }
}
