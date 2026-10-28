using System.Buffers;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpInitialChannelMetadataTests
{
    [Theory]
    [InlineData("")]
    [InlineData("日本語 🎥")]
    public void EncodedInfoRoundTripsThroughExistingParserAndStateAdapter(string text)
    {
        var channelId = new ChannelId(Guid.NewGuid());
        var snapshot = new ObsPublishChannelOptions
        {
            ChannelName = "配信 🎥",
            Genre = text,
            Description = text,
            Comment = text,
            ContactUrl = text.Length == 0 ? "" : "https://example.invalid/日本語",
            BitrateKbps = text.Length == 0 ? null : int.MaxValue,
        }.CreateMetadata();
        var bytes = PcpInitialChannelMetadata.Encode(channelId.Value, snapshot);
        var message = Parse(bytes);
        Assert.Equal(channelId.Value, message.ChannelId);
        Assert.Null(message.Packet);
        Assert.Null(message.Track);
        Assert.Equal("FLV", message.Info!.ContentType);
        var tracker = new ChannelStatusTracker(channelId, new ChannelSessionId(Guid.NewGuid()));
        tracker.TransitionTo(ChannelSessionState.Connecting);
        tracker.TransitionTo(ChannelSessionState.Negotiating);
        var prior = tracker.Snapshot;
        new PcpChannelStateAdapter(tracker).Apply(message);
        Assert.Equal(snapshot, tracker.Snapshot.Metadata);
        Assert.Equal(prior.StreamPosition, tracker.Snapshot.StreamPosition);
        Assert.Equal(prior.State, tracker.Snapshot.State);
        Assert.InRange(bytes.Length, 1, PcpInitialChannelMetadata.MaximumBytes);
    }

    [Fact]
    public void ExactTextByteLimitIncludesNulAndRejectsMalformedOrOversizedDocuments()
    {
        var id = Guid.NewGuid();
        var boundary = new ChannelMetadataSnapshot(name: new string('a', 4095));
        Assert.Equal(4095, Parse(PcpInitialChannelMetadata.Encode(id, boundary)).Info!.Name!.Length);
        Assert.Throws<ArgumentException>(() => PcpInitialChannelMetadata.Encode(id, new(name: new string('a', 4096))));
        Assert.Throws<ArgumentException>(() => PcpInitialChannelMetadata.Encode(id, new(name: "embedded\0nul")));
        Assert.ThrowsAny<ArgumentException>(() => PcpInitialChannelMetadata.Encode(id, new(name: new string((char)0xd800, 1))));
        Assert.Throws<ArgumentException>(() => PcpInitialChannelMetadata.Encode(id, new(comment: "new\nline")));
        var large = new string('a', 4095);
        Assert.Throws<ArgumentException>(() => PcpInitialChannelMetadata.Encode(id, new(large, large, large, large, large, 1, large, large, large)));
        var malformed = PcpInitialChannelMetadata.Encode(id, boundary);
        malformed[^1] = (byte)'x'; // Required terminal NUL.
        Assert.Throws<InvalidDataException>(() => Parse(malformed));
        var complete = PcpInitialChannelMetadata.Encode(id, new(name: "valid"));
        Assert.Throws<InvalidDataException>(() => Parse(complete.AsMemory(0, complete.Length - 1)));
    }

    internal static PcpChannelControlMessage Parse(ReadOnlyMemory<byte> bytes)
    {
        var messages = new List<PcpChannelControlMessage>();
        var result = new PcpAtomDecoder().Decode(new ReadOnlySequence<byte>(bytes), true, new PcpChannelControlSink(messages.Add));
        if (result.Status != PcpDecodeStatus.Completed) throw new InvalidDataException("Incomplete initial metadata.");
        return Assert.Single(messages);
    }
}
