using System.Net;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpUpstreamPortCompatibilityTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7144)]
    [InlineData(65535)]
    public void StationFourByteUpstreamPortPreservesPublicReferral(int port)
    {
        var host = Host(PcpWireValueCodec.EncodeUInt32((uint)port));
        var parsed = PcpRelayHostCandidateParser.TryParse(host);
        Assert.NotNull(parsed);
        Assert.Equal(new PcpRelayHostEndpoint(IPAddress.Parse("8.8.8.8"), 7144), parsed.GlobalEndpoint);
        Assert.Equal(new PcpRelayHostEndpoint(IPAddress.Parse("1.1.1.1"), port), parsed.UpstreamEndpoint);
        Assert.Single(PcpRelayRedirectPlanner.CreatePlan([parsed]));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(65536u)]
    [InlineData(uint.MaxValue)]
    public void OutOfRangeFourByteUpstreamPortIsNotTruncated(uint port) =>
        Assert.Null(PcpRelayHostCandidateParser.TryParse(Host(PcpWireValueCodec.EncodeUInt32(port))));

    [Fact]
    public void LegacyTwoByteUpstreamPortRemainsAccepted() =>
        Assert.Equal(7144, PcpRelayHostCandidateParser.TryParse(
            Host(PcpWireValueCodec.EncodeUInt16(7144)))!.UpstreamEndpoint!.Port);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void MalformedUpstreamPortLengthDoesNotBecomeAReferral(int length) =>
        Assert.Null(PcpRelayHostCandidateParser.TryParse(Host(new byte[length])));

    private static PcpAtom Host(byte[] upstreamPort) => PcpAtom.Parent(PcpKnownAtomNames.Host,
        PcpAtom.Leaf(PcpAtomName.FromAscii("ip"), PcpWireValueCodec.EncodeLegacyIpv4(IPAddress.Parse("8.8.8.8"))),
        PcpAtom.Leaf(PcpKnownAtomNames.HeloPort, PcpWireValueCodec.EncodeUInt16(7144)),
        PcpAtom.Leaf(PcpAtomName.FromAscii("upip"), PcpWireValueCodec.EncodeLegacyIpv4(IPAddress.Parse("1.1.1.1"))),
        PcpAtom.Leaf(PcpAtomName.FromAscii("uppt"), upstreamPort));
}
