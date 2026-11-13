using System.Net;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpPeerDirectoryTests
{
    internal static readonly Guid Channel = Guid.Parse("531dc8df-c7fb-4292-8ac2-c0a626517a87");
    internal static readonly Guid Owner = Guid.Parse("10213243-5465-7687-98a9-babbdcddedef");
    private static readonly PcpPeerSource Source = new(PcpPeerSourceKind.UpstreamHandshake, 1);
    internal static PcpRelayHostCandidate Host(int port = 7144, byte? flags = 0x12, Guid? sid = null) =>
        new(new(IPAddress.Parse("8.8.8.8"), port), new(IPAddress.Parse("192.168.1.2"), port))
        { ChannelId = Channel, PeerSessionId = sid ?? Guid.NewGuid(), Flags = flags };
    private static PcpRelayPeerIdentity Requester => new(Guid.NewGuid(), new(IPAddress.Parse("1.1.1.1"), 7144));

    [Fact]
    public void RegisterUpdateDeduplicateAndPreserveLocalFailureHistory()
    {
        using var clock = new AnnounceManualTimeProvider();
        using var directory = new PcpPeerDirectory(Channel, Owner, clock);
        var host = Host();
        Assert.True(directory.Observe(host, Source));
        directory.RecordConnectionResult(host.GlobalEndpoint!, false);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(directory.Observe(host with { Flags = 0, RelayCount = 3 }, new(PcpPeerSourceKind.UpstreamStream, 2)));
        Assert.Equal(1, directory.Count);
        Assert.Empty(directory.Snapshot());
        clock.Advance(TimeSpan.FromSeconds(29));
        var entry = Assert.Single(directory.Snapshot());
        Assert.Equal(1, entry.FailureCount);
        Assert.False(entry.Host.Receiving);
        Assert.False(entry.Host.RelayAvailable);
        Assert.Equal(3U, entry.Host.RelayCount);
        Assert.Equal(PcpPeerSourceKind.UpstreamStream, entry.Source.Kind);
        directory.RecordConnectionResult(host.GlobalEndpoint!, true);
        Assert.True(Assert.Single(directory.Snapshot()).HasSucceeded);
    }

    [Fact]
    public void EndpointAndSidAliasesMergeWithoutFabricatingAnIdentity()
    {
        using var directory = new PcpPeerDirectory(Channel, Owner);
        var host = Host() with { PeerSessionId = null, Flags = null };
        Assert.True(directory.Observe(host, Source));
        Assert.Empty(PcpReferralPlanner.Select(directory, Requester));
        Assert.Null(Assert.Single(directory.Snapshot()).Host.Receiving);
        var identified = host with { PeerSessionId = Guid.NewGuid() };
        Assert.True(directory.Observe(identified, Source));
        Assert.True(directory.Observe(identified with { GlobalEndpoint = new(IPAddress.Parse("1.0.0.1"), 7145) }, Source));
        Assert.Equal(1, directory.Count);
        Assert.Equal(identified.PeerSessionId, Assert.Single(directory.Snapshot()).Host.PeerSessionId);
        Assert.True(directory.Observe(identified with { PeerSessionId = null, GlobalEndpoint = new(IPAddress.Parse("1.0.0.1"), 7145) }, Source));
        Assert.Equal(identified.PeerSessionId, Assert.Single(directory.Snapshot()).Host.PeerSessionId);
    }

    [Fact]
    public void ExpiryAndCapacityUseMonotonicTimeWithoutTimers()
    {
        using var clock = new AnnounceManualTimeProvider();
        using var directory = new PcpPeerDirectory(Channel, Owner, clock);
        for (var i = 0; i < 257; i++)
        {
            if (i % 64 == 0) clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(directory.Observe(Host(10000 + i), Source));
        }
        Assert.Equal(256, directory.Count);
        Assert.DoesNotContain(directory.Snapshot(), e => e.Host.GlobalEndpoint!.Port == 10000);
        clock.Advance(TimeSpan.FromSeconds(119));
        Assert.NotEmpty(directory.Snapshot());
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(directory.Snapshot());
        Assert.Equal(0, clock.TimerCount);
    }

    [Fact]
    public void ObservationAndFailureWorkRemainBounded()
    {
        using var clock = new AnnounceManualTimeProvider();
        using var directory = new PcpPeerDirectory(Channel, Owner, clock);
        var host = Host();
        for (var i = 0; i < 64; i++) Assert.True(directory.Observe(host, Source));
        Assert.False(directory.Observe(host, Source));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(directory.Observe(host, Source));
        for (var i = 0; i < 1000; i++) directory.RecordConnectionResult(host.GlobalEndpoint!, false);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(64, Assert.Single(directory.Snapshot()).FailureCount);
    }

    [Theory]
    [InlineData("127.0.0.1", 7144)]
    [InlineData("169.254.1.1", 7144)]
    [InlineData("10.1.1.1", 7144)]
    [InlineData("172.16.1.1", 7144)]
    [InlineData("192.168.1.1", 7144)]
    [InlineData("100.64.0.1", 7144)]
    [InlineData("0.0.0.0", 7144)]
    [InlineData("224.0.0.1", 7144)]
    [InlineData("::1", 7144)]
    [InlineData("8.8.8.8", 0)]
    [InlineData("8.8.8.8", -1)]
    [InlineData("8.8.8.8", 65536)]
    public void ProhibitedExternalEndpointsNeverEnterDirectory(string address, int port)
    {
        using var directory = new PcpPeerDirectory(Channel, Owner);
        Assert.False(directory.Observe(Host() with { GlobalEndpoint = new(IPAddress.Parse(address), port) }, Source));
        Assert.Empty(directory.Snapshot());
    }

    [Fact]
    public void ChannelSelfAndDirectLoopsAreExcluded()
    {
        using var directory = new PcpPeerDirectory(Channel, Owner);
        var host = Host();
        Assert.False(directory.Observe(host with { ChannelId = Guid.NewGuid() }, Source));
        Assert.False(directory.Observe(host with { ChannelId = null }, Source));
        Assert.False(directory.Observe(host with { PeerSessionId = Owner }, Source));
        directory.SetLocalEndpoint(host.GlobalEndpoint!);
        Assert.False(directory.Observe(host, Source));
        Assert.False(directory.Observe(Host(7150) with { UpstreamEndpoint = host.GlobalEndpoint }, Source));
        directory.SetLocalEndpoint(new(IPAddress.Parse("1.0.0.1"), 7144));
        Assert.True(directory.Observe(host, Source));
        using (directory.TrackDownstream(3, new(host.PeerSessionId,
            new(IPAddress.Parse("9.9.9.9"), 50000), host.GlobalEndpoint)))
        {
            Assert.Empty(directory.Snapshot());
            Assert.False(directory.Observe(host, Source));
        }
        Assert.Single(directory.Snapshot());
    }

    [Fact]
    public void RetiredOwnerAndDelayedObservationCannotUpdateReplacement()
    {
        using var old = new PcpPeerDirectory(Channel, Owner);
        Action delayed = () => old.Observe(Host(), Source);
        old.Observe(Host(), Source);
        old.Dispose();
        using var replacement = new PcpPeerDirectory(Channel, Guid.NewGuid());
        delayed();
        Assert.False(old.IsActive);
        Assert.Empty(old.Snapshot());
        Assert.Empty(replacement.Snapshot());
    }

    [Fact]
    public void MissingDirectPeerKnowledgeFailsClosedUntilOverflowLeaseEnds()
    {
        using var directory = new PcpPeerDirectory(Channel, Owner);
        directory.Observe(Host(), Source);
        var leases = new List<IDisposable>();
        try
        {
            for (var i = 0; i < 257; i++)
                leases.Add(directory.TrackDownstream(i, new(Guid.NewGuid(),
                    new(IPAddress.Parse("1.1.1.1"), 10000 + i))));
            Assert.Empty(directory.Snapshot());
            leases[^1].Dispose();
            Assert.Single(directory.Snapshot());
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    [InlineData(12, 8)]
    public void PlannerBoundsResults(int count, int expected)
    {
        using var directory = new PcpPeerDirectory(Channel, Owner);
        for (var i = 0; i < count; i++) directory.Observe(Host(10000 + i), Source);
        var chosen = PcpReferralPlanner.Select(directory, Requester);
        Assert.Equal(expected, chosen.Length);
        Assert.Equal(chosen.Length, chosen.Select(h => h.GlobalEndpoint).Distinct().Count());
    }

    [Fact]
    public void PlannerFiltersRequesterUnknownSidAndExplicitFirewallBeforeRanking()
    {
        using var directory = new PcpPeerDirectory(Channel, Owner);
        var requester = Host();
        directory.Observe(requester, Source);
        directory.Observe(Host(7150) with { PeerSessionId = null }, Source);
        directory.Observe(Host(7151, 0x1A), Source);
        Assert.Empty(PcpReferralPlanner.Select(directory, new(requester.PeerSessionId,
            new(IPAddress.Parse("9.9.9.9"), 50000), requester.GlobalEndpoint)));
    }

    [Fact]
    public void PlannerPrefersReceivingThenRelayThenLocalSuccessThenFreshnessThenHops()
    {
        using var clock = new AnnounceManualTimeProvider();
        using var directory = new PcpPeerDirectory(Channel, Owner, clock);
        var receiving = Host(7100, 0x10);
        var available = Host(7101, 0x12);
        var succeeded = Host(7102, 0x12);
        var fresh = Host(7103, 0x12);
        var shortPath = Host(7104, 0x12) with { Hops = 1 };
        directory.Observe(Host(7105, 0x02), Source);
        directory.Observe(receiving, Source);
        directory.Observe(available, Source);
        directory.Observe(succeeded, Source);
        directory.RecordConnectionResult(succeeded.GlobalEndpoint!, true);
        clock.Advance(TimeSpan.FromSeconds(1));
        directory.Observe(fresh, Source);
        directory.Observe(shortPath, Source);
        var ports = PcpReferralPlanner.Select(directory, Requester).Select(h => h.GlobalEndpoint!.Port).ToArray();
        Assert.Equal([7102, 7104, 7103, 7101, 7100, 7105], ports);
    }

    [Fact]
    public void ReferralWireUsesStandardSidCidByteOrderAndRelayBitWithoutLanLeak()
    {
        var host = Host(flags: 0x12) with
        { RelayCount = 7, GlobalEndpoint = new(IPAddress.Parse("8.7.6.5"), 7144) };
        var atom = PcpReferralPlanner.CreateHost(host);
        Assert.Equal(PcpKnownAtomNames.Host, atom.Name);
        Assert.Equal(host.PeerSessionId, PcpWireValueCodec.DecodeGuid(atom.FindFirst(PcpAtomName.FromAscii("id"))!.Content.Span));
        Assert.Equal(Channel, PcpWireValueCodec.DecodeGuid(atom.FindFirst(PcpAtomName.FromAscii("cid"))!.Content.Span));
        Assert.Equal(new byte[] { 0xE8, 0x1B }, atom.FindFirst(PcpKnownAtomNames.HeloPort)!.Content.ToArray());
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, atom.FindFirst(PcpAtomName.FromAscii("ip"))!.Content.ToArray());
        Assert.Equal(new byte[] { 7, 0, 0, 0 }, atom.FindFirst(PcpAtomName.FromAscii("numr"))!.Content.ToArray());
        var parsed = PcpRelayHostCandidateParser.TryParse(atom)!;
        Assert.True(parsed.Receiving);
        Assert.True(parsed.RelayAvailable);
        Assert.False(parsed.Firewalled);
        Assert.Equal(7U, parsed.RelayCount);
        Assert.Null(parsed.LocalEndpoint);
        var unknown = PcpRelayHostCandidateParser.TryParse(PcpReferralPlanner.CreateHost(host with { Flags = null, RelayCount = null }))!;
        Assert.Null(unknown.Receiving);
        Assert.Null(unknown.RelayAvailable);
        Assert.Null(unknown.RelayCount);
    }

    [Fact]
    public void ParserRejectsAmbiguousAndOversizedHostMetadata()
    {
        var host = PcpReferralPlanner.CreateHost(Host());
        var fields = host.Children.ToList();
        fields.Add(fields[0]);
        Assert.Null(PcpRelayHostCandidateParser.TryParse(PcpAtom.Parent(PcpKnownAtomNames.Host, fields)));
        fields = host.Children.ToList();
        fields.Add(PcpAtom.Leaf(PcpAtomName.FromAscii("xtra"), new byte[1024]));
        Assert.Null(PcpRelayHostCandidateParser.TryParse(PcpAtom.Parent(PcpKnownAtomNames.Host, fields)));
        fields = host.Children.ToList();
        fields.Add(PcpAtom.Leaf(PcpAtomName.FromAscii("flg1"), new byte[2]));
        Assert.Null(PcpRelayHostCandidateParser.TryParse(PcpAtom.Parent(PcpKnownAtomNames.Host, fields)));
    }
}
