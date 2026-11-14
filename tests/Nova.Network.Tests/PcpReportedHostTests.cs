using System.Buffers;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpReportedHostTests
{
    internal static PcpAtom Field(string name, byte[] bytes) => PcpAtom.Leaf(PcpAtomName.FromAscii(name), bytes);
    internal static PcpAtom Host(Guid channel) => PcpReferralPlanner.CreateHost(PcpPeerDirectoryTests.Host() with { ChannelId = channel });
    internal static PcpAtom Broadcast(Guid channel, params PcpAtom[] hosts) => PcpAtom.Parent(PcpBroadcastHostReader.Broadcast,
        new[] { Field("cid", PcpWireValueCodec.EncodeGuid(channel)), Field("from", PcpWireValueCodec.EncodeGuid(Guid.NewGuid())),
            Field("ttl", [11]), Field("hops", [0]), Field("grp", [4]) }.Concat(hosts).ToArray());

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void InvalidOrUnsupportedBcstNeverProducesReportedHosts(int failure)
    {
        var cid = PcpPeerDirectoryTests.Channel; var owner = PcpPeerDirectoryTests.Owner;
        var fields = Broadcast(cid, Host(cid)).Children.ToList();
        void Set(string name, byte[] value) { fields.RemoveAll(f => f.Name.ToString() == name); fields.Add(Field(name, value)); }
        switch (failure)
        {
            case 0: Set("ttl", [0]); break;
            case 1: Set("ttl", [33]); break;
            case 2: Set("hops", [255]); break;
            case 3: Set("cid", PcpWireValueCodec.EncodeGuid(Guid.NewGuid())); break;
            case 4: Set("dest", PcpWireValueCodec.EncodeGuid(Guid.NewGuid())); break;
            case 5: Set("from", PcpWireValueCodec.EncodeGuid(owner)); break;
            case 6: fields.Add(Field("from", PcpWireValueCodec.EncodeGuid(Guid.NewGuid()))); break;
            case 7: fields.Add(Broadcast(cid, Host(cid))); break;
            case 8: fields.AddRange(Enumerable.Range(0, 8).Select(_ => Host(cid))); break;
            case 9: fields.Add(Field("xtra", new byte[PcpBroadcastHostReader.MaximumBytes])); break;
            case 10: fields[5] = Host(Guid.NewGuid()); break;
            case 11: Set("grp", [8]); break;
            case 12: Set("hops", [31]); break;
        }
        var atom = PcpAtom.Parent(PcpBroadcastHostReader.Broadcast, fields);
        Assert.Empty(PcpBroadcastHostReader.Read(atom, cid, owner));
        var observed = new List<PcpRelayHostCandidate>();
        var wire = new ArrayBufferWriter<byte>(); PcpAtomTreeCodec.Write(wire, atom);
        var sink = new PcpRelayHostAtomSink(_ => throw new InvalidOperationException("BCST must not enter referrals"), new(cid, owner, observed.Add));
        new PcpAtomDecoder().Decode(new ReadOnlySequence<byte>(wire.WrittenMemory), true, sink);
        Assert.Empty(observed);
    }

    [Fact]
    public void UnknownHostBytesCannotEvadePerHostLimitAndVersionsRequireCorrectLengths()
    {
        var cid = PcpPeerDirectoryTests.Channel; var owner = PcpPeerDirectoryTests.Owner;
        foreach (var bad in new[] { Field("xtra", new byte[1024]), Field("ver", [1, 2]) })
        {
            var host = PcpAtom.Parent(PcpKnownAtomNames.Host, Host(cid).Children.Append(bad).ToArray());
            var atom = Broadcast(cid, host); var wire = new ArrayBufferWriter<byte>(); PcpAtomTreeCodec.Write(wire, atom);
            var observed = new List<PcpRelayHostCandidate>();
            new PcpAtomDecoder().Decode(new ReadOnlySequence<byte>(wire.WrittenMemory), true,
                new PcpRelayHostAtomSink(_ => { }, new(cid, owner, observed.Add)));
            Assert.Empty(observed); Assert.Empty(PcpBroadcastHostReader.Read(atom, cid, owner));
        }
    }

    [Fact]
    public void LocalGuiContractCopiesValuesWithoutSharingMutableAddressesOrControlObjects()
    {
        using var directory = new PcpPeerDirectory(PcpPeerDirectoryTests.Channel, PcpPeerDirectoryTests.Owner);
        directory.ObserveReport(PcpPeerDirectoryTests.Host(), new());
        var local = directory.ObserveLocalTree();
        var report = Assert.Single(local.Reports);
        Assert.Equal(0x08080808U, report.Host.GlobalEndpoint!.Address);
        Assert.Equal("8.8.8.8", report.Host.GlobalEndpoint.AddressText);
        Assert.Throws<NotSupportedException>(() => ((IList<PcpLocalReportedHostObservation>)local.Reports).Clear());
        Assert.Equal("ObserveLocalRelayTree", Assert.Single(typeof(IPcpLocalRelayTreeSource).GetMethods()).Name);
        foreach (var type in new[] { typeof(PcpLocalRelayTreeObservation), typeof(PcpLocalHostInfo), typeof(PcpLocalRelayEndpoint),
            typeof(PcpLocalPeerConnection), typeof(PcpLocalPeerIdentity), typeof(PcpLocalReportedHostObservation) })
            Assert.DoesNotContain(type.GetProperties(), p => p.PropertyType == typeof(System.Net.IPAddress) ||
                typeof(IDisposable).IsAssignableFrom(p.PropertyType) || typeof(Stream).IsAssignableFrom(p.PropertyType) ||
                typeof(Delegate).IsAssignableFrom(p.PropertyType));
    }

    [Fact]
    public void ReportStorageHasIndependentMeaningFiniteWorkExpiryAndRetirement()
    {
        using var clock = new AnnounceManualTimeProvider();
        using var directory = new PcpPeerDirectory(PcpPeerDirectoryTests.Channel, PcpPeerDirectoryTests.Owner, clock);
        var host = PcpPeerDirectoryTests.Host();
        for (var i = 0; i < 65; i++) directory.ObserveReport(host with { PeerSessionId = Guid.NewGuid() }, new());
        Assert.Equal(64, directory.ObserveLocalTree().Reports.Count); Assert.Empty(directory.Snapshot());
        for (var batch = 0; batch < 4; batch++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            for (var i = 0; i < 64; i++) directory.ObserveReport(host with { PeerSessionId = Guid.NewGuid() }, new());
        }
        Assert.Equal(PcpPeerDirectory.MaximumCandidates, directory.ObserveLocalTree().Reports.Count);
        Assert.Equal(0, clock.TimerCount);
        clock.Advance(PcpPeerDirectory.Lifetime); Assert.Empty(directory.ObserveLocalTree().Reports);
        Assert.False(directory.Observe(host with { GlobalEndpoint = new(System.Net.IPAddress.Loopback, 7144), LocalEndpoint = null }, new()));
        Assert.Single(directory.ObserveLocalTree().Reports); Assert.Empty(directory.Snapshot());
        directory.Dispose(); directory.ObserveReport(host, new()); Assert.Empty(directory.ObserveLocalTree().Reports);
    }
}
