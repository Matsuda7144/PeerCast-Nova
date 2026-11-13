using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpChannelStateAdapterTests
{
    [Fact]
    public void NewAttemptRequiresFreshAdmissionBitrateWithoutErasingMediaStatus()
    {
        var tracker = CreateReceivingTracker();
        var adapter = new PcpChannelStateAdapter(tracker);
        adapter.Apply(new(ChannelGuid, null, CreateFullInfo(), null));
        Assert.Equal(512, adapter.DeclaredBitrateKbps);
        var before = tracker.Snapshot;
        adapter.BeginAttempt();
        Assert.Null(adapter.DeclaredBitrateKbps);
        Assert.Same(before, tracker.Snapshot);
        adapter.Apply(new(ChannelGuid, null, CreateFullInfo() with { BitrateKbps = null }, null));
        Assert.Null(adapter.DeclaredBitrateKbps);
        Assert.Equal(512, tracker.Snapshot.Metadata.BitrateKbps);
        adapter.Apply(new(ChannelGuid, null, CreateFullInfo() with { BitrateKbps = 0 }, null));
        Assert.Null(adapter.DeclaredBitrateKbps);
        adapter.Apply(new(ChannelGuid, null, CreateFullInfo() with { BitrateKbps = 800 }, null));
        Assert.Equal(800, adapter.DeclaredBitrateKbps);
    }

    private static readonly Guid ChannelGuid = Guid.ParseExact(
        "531DC8DFC7FB42928AC2C0A626517A87",
        "N");

    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact("3F2504E04F8911D39A0C0305E82C3301", "N"));

    [Fact]
    public void Apply_InfoTrackAndPacketUseOneRevision()
    {
        var tracker = CreateReceivingTracker();
        var adapter = new PcpChannelStateAdapter(tracker);
        var before = tracker.Snapshot;
        var message = new PcpChannelControlMessage(
            ChannelGuid,
            new PcpChannelPacketHeader(1, 1024, 4096),
            CreateFullInfo(),
            CreateFullTrack());

        var result = adapter.Apply(message);

        Assert.Equal(before.Revision + 1, result.Snapshot.Revision);
        Assert.Equal(1024, result.Snapshot.StreamPosition);
        Assert.Equal("Synthetic channel", result.Snapshot.Metadata.Name);
        Assert.Equal("RAW", result.Snapshot.Metadata.StreamType);
        Assert.Equal("Synthetic comment", result.Snapshot.Metadata.Comment);
        Assert.Equal(".test", result.Snapshot.Metadata.StreamExtension);
        Assert.Equal(512, result.Snapshot.Metadata.BitrateKbps);
        Assert.Equal(
            "Synthetic title",
            result.Snapshot.Metadata.Track?.Title);
        Assert.Equal(
            "Synthetic creator",
            result.Snapshot.Metadata.Track?.Creator);
        Assert.Same(message.Packet, result.Packet);
        Assert.Null(result.Discontinuity);
    }

    [Fact]
    public void Apply_PartialMetadataPreservesMissingAndAppliesEmptyValues()
    {
        var tracker = CreateReceivingTracker();
        var adapter = new PcpChannelStateAdapter(tracker);
        adapter.Apply(new PcpChannelControlMessage(
            ChannelGuid,
            Packet: null,
            CreateFullInfo(),
            CreateFullTrack()));
        var before = tracker.Snapshot;
        var partialInfo = new PcpChannelInfo(
            ContentType: null,
            StreamType: null,
            Name: string.Empty,
            Genre: null,
            Description: null,
            Comment: null,
            Url: null,
            StreamExtension: null,
            BitrateKbps: null);
        var partialTrack = new PcpChannelTrack(
            Title: string.Empty,
            Genre: null,
            Album: null,
            Creator: null,
            Url: null);

        var result = adapter.Apply(new PcpChannelControlMessage(
            ChannelGuid,
            Packet: null,
            partialInfo,
            partialTrack));

        Assert.Equal(before.Revision + 1, result.Snapshot.Revision);
        Assert.Equal(string.Empty, result.Snapshot.Metadata.Name);
        Assert.Equal("Synthetic genre", result.Snapshot.Metadata.Genre);
        Assert.Equal(
            string.Empty,
            result.Snapshot.Metadata.Track?.Title);
        Assert.Equal(
            "Synthetic creator",
            result.Snapshot.Metadata.Track?.Creator);
    }

    [Fact]
    public void Apply_UnwrapsPositionAndMakesDuplicatesIdempotent()
    {
        var tracker = CreateReceivingTracker();
        var adapter = new PcpChannelStateAdapter(tracker);

        var first = adapter.Apply(CreatePacketMessage(0xFFFFFFF0U));
        var wrapped = adapter.Apply(CreatePacketMessage(0x00000020U));
        var duplicate = adapter.Apply(CreatePacketMessage(0x00000020U));

        Assert.Equal(4294967280L, first.Snapshot.StreamPosition);
        Assert.Equal(4294967328L, wrapped.Snapshot.StreamPosition);
        Assert.Null(wrapped.Discontinuity);
        Assert.Same(wrapped.Snapshot, duplicate.Snapshot);
    }

    [Fact]
    public void Apply_BackwardPositionReportsDiscontinuityWithoutMovingCore()
    {
        var tracker = CreateReceivingTracker();
        var adapter = new PcpChannelStateAdapter(tracker);
        var first = adapter.Apply(CreatePacketMessage(1000));

        var backward = adapter.Apply(CreatePacketMessage(900));
        var recovered = adapter.Apply(CreatePacketMessage(1100));

        Assert.Same(first.Snapshot, backward.Snapshot);
        Assert.Equal(1000U, backward.Discontinuity?.PreviousWirePosition);
        Assert.Equal(900U, backward.Discontinuity?.ObservedWirePosition);
        Assert.Equal(
            1000,
            backward.Discontinuity?.AbsolutePositionBeforeObservation);
        Assert.Equal(1100, recovered.Snapshot.StreamPosition);
    }

    [Fact]
    public void Apply_ChannelMismatchThrowsBeforeMutation()
    {
        var tracker = CreateReceivingTracker();
        var adapter = new PcpChannelStateAdapter(tracker);
        var before = tracker.Snapshot;
        var otherChannel = new PcpChannelControlMessage(
            Guid.ParseExact("6F9619FF8B86D011B42D00C04FC964FF", "N"),
            new PcpChannelPacketHeader(1, 100, 10),
            Info: null,
            Track: null);

        Assert.Throws<InvalidDataException>(() => adapter.Apply(otherChannel));
        Assert.Same(before, tracker.Snapshot);
    }

    [Fact]
    public void Apply_BitrateOutsideCoreRangeThrowsBeforePositionMutation()
    {
        var tracker = CreateReceivingTracker();
        var adapter = new PcpChannelStateAdapter(tracker);
        var before = tracker.Snapshot;
        var info = new PcpChannelInfo(
            ContentType: null,
            StreamType: null,
            Name: null,
            Genre: null,
            Description: null,
            Comment: null,
            Url: null,
            StreamExtension: null,
            BitrateKbps: (uint)int.MaxValue + 1U);
        var message = new PcpChannelControlMessage(
            ChannelGuid,
            new PcpChannelPacketHeader(1, 100, 10),
            info,
            Track: null);

        Assert.Throws<InvalidDataException>(() => adapter.Apply(message));
        Assert.Same(before, tracker.Snapshot);

        var accepted = adapter.Apply(CreatePacketMessage(100));
        Assert.Equal(100, accepted.Snapshot.StreamPosition);
    }

    private static PcpChannelControlMessage CreatePacketMessage(uint position)
    {
        return new PcpChannelControlMessage(
            ChannelGuid,
            new PcpChannelPacketHeader(1, position, 10),
            Info: null,
            Track: null);
    }

    private static PcpChannelInfo CreateFullInfo()
    {
        return new PcpChannelInfo(
            "video/test",
            "RAW",
            "Synthetic channel",
            "Synthetic genre",
            "Synthetic description",
            "Synthetic comment",
            "https://example.invalid/channel",
            ".test",
            512);
    }

    private static PcpChannelTrack CreateFullTrack()
    {
        return new PcpChannelTrack(
            "Synthetic title",
            "Synthetic track genre",
            "Synthetic album",
            "Synthetic creator",
            "https://example.invalid/track");
    }

    private static ChannelStatusTracker CreateReceivingTracker()
    {
        var tracker = new ChannelStatusTracker(
            new ChannelId(ChannelGuid),
            SessionId);
        tracker.TransitionTo(ChannelSessionState.Connecting);
        tracker.TransitionTo(ChannelSessionState.Negotiating);
        tracker.TransitionTo(ChannelSessionState.Receiving);
        return tracker;
    }
}
