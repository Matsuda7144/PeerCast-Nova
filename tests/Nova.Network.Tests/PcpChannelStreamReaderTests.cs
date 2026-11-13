using System.Buffers;
using System.IO.Pipelines;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpChannelStreamReaderTests
{
    private static readonly Guid ChannelGuid = Guid.ParseExact(
        "531DC8DFC7FB42928AC2C0A626517A87",
        "N");

    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact("3F2504E04F8911D39A0C0305E82C3301", "N"));

    [Fact]
    public void Options_RejectInconsistentPacketAndTextLimits()
    {
        Assert.Throws<InvalidOperationException>(
            () => new PcpChannelStreamOptions
            {
                MaxAtomContentBytes = 4,
                MaxPacketBytes = 5,
                MaxTextBytes = 4,
            }.Validate());

        Assert.Throws<InvalidOperationException>(
            () => new PcpChannelStreamOptions
            {
                MaxAtomContentBytes = 4,
                MaxPacketBytes = 4,
                MaxTextBytes = 5,
            }.Validate());
    }

    [Fact]
    public async Task ReadToEnd_SegmentedPacketReachesConsumerAndCore()
    {
        var pipe = new Pipe();
        var tracker = CreateReceivingTracker();
        var consumer = new CollectingPacketConsumer();
        var reader = CreateReader(pipe, tracker, consumer);
        var bytes = Encode(CreatePacketAtom(4096, [0x10, 0x20, 0x30]));

        var readTask = reader.ReadToEndAsync();
        await WriteInChunksAsync(pipe.Writer, bytes, chunkSize: 1);
        var result = await readTask;

        Assert.Equal(
            PcpChannelStreamEndReason.ConnectionEnded,
            result.EndReason);
        Assert.Equal(1, result.ChannelMessageCount);
        Assert.Equal(1, result.PacketCount);
        Assert.Equal(3, result.PayloadByteCount);

        var packet = Assert.Single(consumer.Packets);
        Assert.Equal(0, packet.Sequence);
        Assert.Equal(1U, packet.Type);
        Assert.Equal(4096U, packet.WirePosition);
        Assert.Equal(4096, packet.StreamPosition);
        Assert.Equal([0x10, 0x20, 0x30], packet.Payload);
        Assert.Null(packet.Discontinuity);
        Assert.Equal(4096, tracker.Snapshot.StreamPosition);
    }

    [Fact]
    public async Task ReadToEnd_MetadataOnlyMessageUpdatesCoreWithoutPacket()
    {
        var pipe = new Pipe();
        var tracker = CreateReceivingTracker();
        var consumer = new CollectingPacketConsumer();
        var reader = CreateReader(pipe, tracker, consumer);
        var atom = PcpAtom.Parent(
            PcpKnownAtomNames.Channel,
            PcpAtom.Leaf(
                PcpKnownAtomNames.ChannelId,
                PcpWireValueCodec.EncodeGuid(ChannelGuid)),
            PcpAtom.Parent(
                PcpKnownAtomNames.ChannelInfo,
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Name,
                    PcpWireValueCodec.EncodeNullTerminatedUtf8(
                        "Synthetic channel"))));

        var readTask = reader.ReadToEndAsync();
        await WriteInChunksAsync(
            pipe.Writer,
            Encode(atom),
            chunkSize: 3);
        var result = await readTask;

        Assert.Equal(1, result.ChannelMessageCount);
        Assert.Equal(0, result.PacketCount);
        Assert.Empty(consumer.Packets);
        Assert.Equal("Synthetic channel", tracker.Snapshot.Metadata.Name);
    }

    [Fact]
    public async Task ReadToEnd_AwaitsConsumerBeforeDecodingNextAtom()
    {
        var pipe = new Pipe();
        var tracker = CreateReceivingTracker();
        var consumer = new GatedPacketConsumer();
        var reader = CreateReader(pipe, tracker, consumer);
        var bytes = Encode(
            CreatePacketAtom(100, [0x01]),
            CreatePacketAtom(200, [0x02]));
        var readTask = reader.ReadToEndAsync();

        await pipe.Writer.WriteAsync(bytes);
        await pipe.Writer.CompleteAsync();
        await consumer.FirstPacketEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(2));

        Assert.Equal(1, consumer.CallCount);
        Assert.Equal(100, tracker.Snapshot.StreamPosition);

        consumer.ReleaseFirstPacket.TrySetResult();
        var result = await readTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, consumer.CallCount);
        Assert.Equal(2, result.PacketCount);
        Assert.Equal(200, tracker.Snapshot.StreamPosition);
    }

    [Fact]
    public async Task ReadToEnd_ConsumerCancellationStopsSession()
    {
        var pipe = new Pipe();
        var tracker = CreateReceivingTracker();
        var consumer = new CancelablePacketConsumer();
        var reader = CreateReader(pipe, tracker, consumer);
        using var cancellationSource = new CancellationTokenSource();
        var readTask = reader.ReadToEndAsync(cancellationSource.Token);

        await pipe.Writer.WriteAsync(
            Encode(CreatePacketAtom(100, [0x01, 0x02])));
        await pipe.Writer.CompleteAsync();
        await consumer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await readTask);
    }

    [Fact]
    public async Task ReadToEnd_IdlePipeTimesOutWithoutCallerCancellation()
    {
        var pipe = new Pipe();
        var tracker = CreateReceivingTracker();
        var consumer = new CollectingPacketConsumer();
        var options = new PcpChannelStreamOptions
        {
            ReadIdleTimeout = TimeSpan.FromMilliseconds(25),
        };
        var reader = CreateReader(pipe, tracker, consumer, options);

        await Assert.ThrowsAsync<TimeoutException>(
            () => reader.ReadToEndAsync());

        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task ReadToEnd_RejectsPacketBeforeCopyWhenLimitIsExceeded()
    {
        var pipe = new Pipe();
        var tracker = CreateReceivingTracker();
        var consumer = new CollectingPacketConsumer();
        var options = new PcpChannelStreamOptions
        {
            MaxAtomContentBytes = 32,
            MaxPacketBytes = 4,
            MaxTextBytes = 16,
        };
        var reader = CreateReader(pipe, tracker, consumer, options);
        var readTask = reader.ReadToEndAsync();

        await pipe.Writer.WriteAsync(
            Encode(CreatePacketAtom(100, [0x01, 0x02, 0x03, 0x04, 0x05])));
        await pipe.Writer.CompleteAsync();

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await readTask);
        Assert.Empty(consumer.Packets);
        Assert.Null(tracker.Snapshot.StreamPosition);
    }

    [Theory]
    [InlineData(0U, PcpQuitReason.Unspecified)]
    [InlineData(1000U, PcpQuitReason.Normal)]
    [InlineData(1003U, PcpQuitReason.Unavailable)]
    [InlineData(1005U, PcpQuitReason.NotIdentified)]
    [InlineData(1007U, PcpQuitReason.BadAgent)]
    [InlineData(1234U, PcpQuitReason.Other)]
    public async Task ReadToEnd_QuitTerminatesWithoutPacketDelivery(uint code, PcpQuitReason reason)
    {
        var pipe = new Pipe();
        var tracker = CreateReceivingTracker();
        var consumer = new CollectingPacketConsumer();
        var reader = CreateReader(pipe, tracker, consumer);
        var quit = PcpAtom.Leaf(
            PcpKnownAtomNames.Quit,
            PcpWireValueCodec.EncodeUInt32(code));
        var readTask = reader.ReadToEndAsync();

        await pipe.Writer.WriteAsync(Encode(quit));
        await pipe.Writer.CompleteAsync();
        var result = await readTask;

        Assert.Equal(PcpChannelStreamEndReason.QuitReceived, result.EndReason);
        Assert.Equal(new PcpQuitInfo(code), result.Quit);
        Assert.Equal(reason, result.Quit!.Value.Reason);
        Assert.Equal(0, result.ChannelMessageCount);
        Assert.Equal(0, result.PacketCount);
        Assert.Empty(consumer.Packets);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(8)]
    public async Task ReadToEnd_MalformedQuitDoesNotUndoDeliveredMedia(int length)
    {
        var pipe = new Pipe();
        var tracker = CreateReceivingTracker();
        var consumer = new CollectingPacketConsumer();
        var reader = CreateReader(pipe, tracker, consumer);
        var quit = length < 0 ? PcpAtom.Parent(PcpKnownAtomNames.Quit) :
            PcpAtom.Leaf(PcpKnownAtomNames.Quit, new byte[length]);
        var read = reader.ReadToEndAsync();
        await pipe.Writer.WriteAsync(Encode(CreatePacketAtom(100, [0x11, 0x22]), quit));
        await pipe.Writer.CompleteAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => read);
        Assert.Equal(new byte[] { 0x11, 0x22 }, Assert.Single(consumer.Packets).Payload);
        Assert.Equal(100, tracker.Snapshot.StreamPosition);
        Assert.Equal(SessionId, tracker.Snapshot.SessionId);
    }

    [Fact]
    public async Task ReadToEnd_DiscontinuityIsDeliveredWithPacketData()
    {
        var pipe = new Pipe();
        var tracker = CreateReceivingTracker();
        var consumer = new CollectingPacketConsumer();
        var reader = CreateReader(pipe, tracker, consumer);
        var readTask = reader.ReadToEndAsync();

        await pipe.Writer.WriteAsync(
            Encode(
                CreatePacketAtom(1000, [0x01]),
                CreatePacketAtom(900, [0x02])));
        await pipe.Writer.CompleteAsync();
        var result = await readTask;

        Assert.Equal(2, result.PacketCount);
        Assert.Equal(1000, tracker.Snapshot.StreamPosition);
        Assert.Equal(1000, consumer.Packets[0].StreamPosition);
        Assert.Null(consumer.Packets[0].Discontinuity);
        Assert.Null(consumer.Packets[1].StreamPosition);
        Assert.Equal(
            900U,
            consumer.Packets[1].Discontinuity?.ObservedWirePosition);
        Assert.Equal([0x02], consumer.Packets[1].Payload);
    }

    private static PcpChannelStreamReader CreateReader(
        Pipe pipe,
        ChannelStatusTracker tracker,
        IPcpChannelPacketConsumer consumer,
        PcpChannelStreamOptions? options = null)
    {
        return new PcpChannelStreamReader(
            pipe.Reader,
            new PcpChannelStateAdapter(tracker),
            consumer,
            options);
    }

    private static PcpAtom CreatePacketAtom(
        uint position,
        byte[] payload)
    {
        return PcpAtom.Parent(
            PcpKnownAtomNames.Channel,
            PcpAtom.Leaf(
                PcpKnownAtomNames.ChannelId,
                PcpWireValueCodec.EncodeGuid(ChannelGuid)),
            PcpAtom.Parent(
                PcpKnownAtomNames.ChannelPacket,
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Type,
                    PcpWireValueCodec.EncodeUInt32(1)),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Position,
                    PcpWireValueCodec.EncodeUInt32(position)),
                PcpAtom.Leaf(PcpKnownAtomNames.Data, payload)));
    }

    private static byte[] Encode(params PcpAtom[] atoms)
    {
        var writer = new ArrayBufferWriter<byte>();

        foreach (var atom in atoms)
        {
            PcpAtomTreeCodec.Write(writer, atom);
        }

        return writer.WrittenMemory.ToArray();
    }

    private static async Task WriteInChunksAsync(
        PipeWriter writer,
        byte[] bytes,
        int chunkSize)
    {
        try
        {
            for (var offset = 0; offset < bytes.Length; offset += chunkSize)
            {
                var count = Math.Min(chunkSize, bytes.Length - offset);
                await writer.WriteAsync(
                    bytes.AsMemory(offset, count));
            }
        }
        finally
        {
            await writer.CompleteAsync();
        }
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

    private sealed class CollectingPacketConsumer : IPcpChannelPacketConsumer
    {
        public List<ObservedPacket> Packets { get; } = [];

        public ValueTask ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Packets.Add(new ObservedPacket(
                packet.Sequence,
                packet.Type,
                packet.WirePosition,
                packet.StreamPosition,
                packet.Payload.ToArray(),
                packet.Discontinuity));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class GatedPacketConsumer : IPcpChannelPacketConsumer
    {
        private int _callCount;

        public TaskCompletionSource FirstPacketEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstPacket { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => Volatile.Read(ref _callCount);

        public async ValueTask ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _callCount);

            if (call == 1)
            {
                FirstPacketEntered.TrySetResult();
                await ReleaseFirstPacket.Task.WaitAsync(cancellationToken);
            }

            _ = packet.Payload.Span[0];
        }
    }

    private sealed class CancelablePacketConsumer : IPcpChannelPacketConsumer
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken)
        {
            _ = packet.Payload.Length;
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed record ObservedPacket(
        long Sequence,
        uint Type,
        uint WirePosition,
        long? StreamPosition,
        byte[] Payload,
        PcpStreamDiscontinuity? Discontinuity);
}
