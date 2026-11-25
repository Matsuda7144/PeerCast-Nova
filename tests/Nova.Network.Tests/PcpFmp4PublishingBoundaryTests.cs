using System.Collections.Concurrent;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpFmp4PublishingBoundaryTests
{
    private const uint HeadPacketType = 0x64616568;
    private const uint DataPacketType = 0x61746164;

    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact("4A06648A25D140B8A8B411B91D084E1A", "N"));
    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact("348A107E4C72415F8D0D1D8378A60391", "N"));

    [Fact]
    public async Task Initialization_is_one_complete_head_with_owned_identity_and_zero_positions()
    {
        var consumer = new RecordingConsumer();
        await using var boundary = CreateBoundary(consumer);
        byte[] initialization = [0x00, 0x00, 0x00, 0x08, 0x66, 0x74, 0x79, 0x70];

        Assert.Equal(
            PcpFmp4PublishingResult.Published,
            await boundary.PublishSegmentAsync(
                initialization,
                PcpFmp4PublishingSegmentRole.Initialization));

        var packet = Assert.Single(consumer.Packets);
        Assert.Equal(ChannelId, packet.ChannelId);
        Assert.Equal(SessionId, packet.SessionId);
        Assert.Equal(0, packet.Sequence);
        Assert.Equal(HeadPacketType, packet.Type);
        Assert.Equal(0U, packet.WirePosition);
        Assert.Equal(0L, packet.StreamPosition);
        Assert.Equal(initialization, packet.Payload);

        var snapshot = boundary.Snapshot;
        Assert.Equal(PcpFmp4PublishingBoundaryState.Ready, snapshot.State);
        Assert.Equal(PcpFmp4PublishingResult.Published, snapshot.Result);
        Assert.Equal(1, snapshot.InitializationSegmentCount);
        Assert.Equal(0, snapshot.MediaSegmentCount);
        Assert.Equal(1, snapshot.AcceptedPacketCount);
        Assert.Equal(initialization.Length, snapshot.AcceptedByteCount);
        Assert.Equal(1, snapshot.HeadPacketCount);
        Assert.Equal(0, snapshot.DataPacketCount);
        Assert.Equal(1, snapshot.NextSequence);
        Assert.Equal((uint)initialization.Length, snapshot.NextWirePosition);
        Assert.Equal((long)initialization.Length, snapshot.NextStreamPosition);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(9, 3)]
    public async Task Media_is_split_at_packet_boundaries_and_reassembles_exactly(
        int mediaLength,
        int expectedMediaPacketCount)
    {
        var consumer = new RecordingConsumer();
        await using var boundary = CreateBoundary(
            consumer,
            new PcpFmp4PublishingBoundaryOptions
            {
                MaximumInitializationBytes = 4,
                MaximumMediaSegmentBytes = 16,
                MaximumPacketPayloadBytes = 4,
            });
        byte[] initialization = [0x10, 0x11, 0x12];
        var media = Enumerable.Range(0, mediaLength)
            .Select(static index => (byte)(0x40 + index))
            .ToArray();

        Assert.Equal(
            PcpFmp4PublishingResult.Published,
            await boundary.PublishSegmentAsync(
                initialization,
                PcpFmp4PublishingSegmentRole.Initialization));
        Assert.Equal(
            PcpFmp4PublishingResult.Published,
            await boundary.PublishSegmentAsync(
                media,
                PcpFmp4PublishingSegmentRole.Media));

        var packets = consumer.Packets;
        Assert.Equal(expectedMediaPacketCount + 1, packets.Count);
        Assert.All(packets, packet => Assert.InRange(packet.Payload.Length, 1, 4));
        Assert.Equal(HeadPacketType, packets[0].Type);
        var dataPackets = packets.Skip(1).ToArray();
        Assert.All(dataPackets, packet =>
            Assert.Equal(DataPacketType, packet.Type));
        Assert.Equal(media, dataPackets.SelectMany(static packet => packet.Payload).ToArray());
        Assert.All(
            packets.Select((packet, index) => (packet, index)),
            item => Assert.Equal(item.index, item.packet.Sequence));

        var expectedPosition = 0L;
        foreach (var packet in packets)
        {
            Assert.Equal((uint)expectedPosition, packet.WirePosition);
            Assert.Equal(expectedPosition, packet.StreamPosition);
            expectedPosition += packet.Payload.Length;
        }

        var snapshot = boundary.Snapshot;
        Assert.Equal(1, snapshot.InitializationSegmentCount);
        Assert.Equal(1, snapshot.MediaSegmentCount);
        Assert.Equal(expectedMediaPacketCount + 1, snapshot.AcceptedPacketCount);
        Assert.Equal(initialization.Length + media.Length, snapshot.AcceptedByteCount);
        Assert.Equal(1, snapshot.HeadPacketCount);
        Assert.Equal(expectedMediaPacketCount, snapshot.DataPacketCount);
        Assert.Equal(expectedMediaPacketCount + 1, snapshot.NextSequence);
        Assert.Equal((uint)expectedPosition, snapshot.NextWirePosition);
        Assert.Equal(expectedPosition, snapshot.NextStreamPosition);
    }

    [Fact]
    public void Invalid_options_and_identities_are_rejected_at_creation()
    {
        var consumer = new RecordingConsumer();

        Assert.Equal(
            PcpFmp4PublishingBoundaryCreateResult.InvalidConfiguration,
            PcpFmp4PublishingBoundary.Create(
                ChannelId,
                SessionId,
                consumer,
                out var invalidBoundary,
                new PcpFmp4PublishingBoundaryOptions
                {
                    MaximumInitializationBytes = 5,
                    MaximumPacketPayloadBytes = 4,
                }));
        Assert.Null(invalidBoundary);

        Assert.Equal(
            PcpFmp4PublishingBoundaryCreateResult.InvalidIdentity,
            PcpFmp4PublishingBoundary.Create(
                null,
                SessionId,
                consumer,
                out invalidBoundary));
        Assert.Null(invalidBoundary);

        Assert.Equal(
            PcpFmp4PublishingBoundaryCreateResult.InvalidConsumer,
            PcpFmp4PublishingBoundary.Create(
                ChannelId,
                SessionId,
                null,
                out invalidBoundary));
        Assert.Null(invalidBoundary);
    }

    [Fact]
    public async Task Empty_oversized_and_out_of_order_segments_are_rejected_without_emission()
    {
        var consumer = new RecordingConsumer();
        await using var boundary = CreateBoundary(
            consumer,
            new PcpFmp4PublishingBoundaryOptions
            {
                MaximumInitializationBytes = 4,
                MaximumMediaSegmentBytes = 8,
                MaximumPacketPayloadBytes = 4,
            });

        Assert.Equal(
            PcpFmp4PublishingResult.InvalidSegment,
            await boundary.PublishSegmentAsync(
                ReadOnlyMemory<byte>.Empty,
                PcpFmp4PublishingSegmentRole.Initialization));
        Assert.Equal(
            PcpFmp4PublishingResult.InitializationRequired,
            await boundary.PublishSegmentAsync(
                new byte[] { 0x20 },
                PcpFmp4PublishingSegmentRole.Media));
        Assert.Equal(
            PcpFmp4PublishingResult.InitializationSegmentTooLarge,
            await boundary.PublishSegmentAsync(
                new byte[5],
                PcpFmp4PublishingSegmentRole.Initialization));
        Assert.Empty(consumer.Packets);

        Assert.Equal(
            PcpFmp4PublishingResult.Published,
            await boundary.PublishSegmentAsync(
                new byte[] { 0x01, 0x02 },
                PcpFmp4PublishingSegmentRole.Initialization));
        Assert.Equal(
            PcpFmp4PublishingResult.InitializationAlreadyPublished,
            await boundary.PublishSegmentAsync(
                new byte[] { 0x03 },
                PcpFmp4PublishingSegmentRole.Initialization));
        Assert.Equal(
            PcpFmp4PublishingResult.InvalidSegment,
            await boundary.PublishSegmentAsync(
                ReadOnlyMemory<byte>.Empty,
                PcpFmp4PublishingSegmentRole.Media));
        Assert.Equal(
            PcpFmp4PublishingResult.MediaSegmentTooLarge,
            await boundary.PublishSegmentAsync(
                new byte[9],
                PcpFmp4PublishingSegmentRole.Media));
        Assert.Single(consumer.Packets);
        Assert.Equal(1, boundary.Snapshot.InitializationSegmentCount);
        Assert.Equal(0, boundary.Snapshot.MediaSegmentCount);
    }

    [Fact]
    public async Task Invalid_role_is_rejected_without_faulting_the_boundary()
    {
        var consumer = new RecordingConsumer();
        await using var boundary = CreateBoundary(consumer);

        Assert.Equal(
            PcpFmp4PublishingResult.InvalidSegmentRole,
            await boundary.PublishSegmentAsync(new byte[] { 1 }, (PcpFmp4PublishingSegmentRole)42));
        Assert.Equal(PcpFmp4PublishingBoundaryState.AwaitingInitialization,
            boundary.Snapshot.State);
        Assert.Empty(consumer.Packets);
    }

    [Fact]
    public async Task Publication_awaits_consumer_and_rejects_overlapping_calls()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new RecordingConsumer
        {
            BeforeAccept = async (_, token) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            },
        };
        await using var boundary = CreateBoundary(consumer);

        var initializationTask = boundary.PublishSegmentAsync(
            new byte[] { 1 },
            PcpFmp4PublishingSegmentRole.Initialization).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, boundary.Snapshot.AcceptedPacketCount);
        Assert.Equal(
            PcpFmp4PublishingResult.ConcurrentPublishRejected,
            await boundary.PublishSegmentAsync(
                new byte[] { 2 },
                PcpFmp4PublishingSegmentRole.Media));
        Assert.False(initializationTask.IsCompleted);

        release.TrySetResult();
        Assert.Equal(PcpFmp4PublishingResult.Published, await initializationTask);
        Assert.Equal(1, boundary.Snapshot.AcceptedPacketCount);
        Assert.Equal(1, boundary.Snapshot.InitializationSegmentCount);
    }

    [Fact]
    public async Task Interior_consumer_failure_counts_only_packets_accepted_before_failure()
    {
        var consumer = new RecordingConsumer
        {
            BeforeAccept = (call, _) => call == 3
                ? ValueTask.FromException(new IOException("injected consumer failure"))
                : ValueTask.CompletedTask,
        };
        await using var boundary = CreateBoundary(
            consumer,
            new PcpFmp4PublishingBoundaryOptions
            {
                MaximumInitializationBytes = 4,
                MaximumMediaSegmentBytes = 16,
                MaximumPacketPayloadBytes = 4,
            });

        Assert.Equal(
            PcpFmp4PublishingResult.Published,
            await boundary.PublishSegmentAsync(new byte[] { 1 }, PcpFmp4PublishingSegmentRole.Initialization));
        Assert.Equal(
            PcpFmp4PublishingResult.ConsumerRejected,
            await boundary.PublishSegmentAsync(new byte[9], PcpFmp4PublishingSegmentRole.Media));

        Assert.Equal(3, consumer.CallCount);
        Assert.Equal(2, consumer.Packets.Count);
        var snapshot = boundary.Snapshot;
        Assert.Equal(PcpFmp4PublishingBoundaryState.Faulted, snapshot.State);
        Assert.Equal(PcpFmp4PublishingResult.ConsumerRejected, snapshot.Result);
        Assert.Equal(1, snapshot.InitializationSegmentCount);
        Assert.Equal(0, snapshot.MediaSegmentCount);
        Assert.Equal(2, snapshot.AcceptedPacketCount);
        Assert.Equal(5, snapshot.AcceptedByteCount);
        Assert.Equal(1, snapshot.HeadPacketCount);
        Assert.Equal(1, snapshot.DataPacketCount);
        Assert.Equal(1, snapshot.ConsumerFailureCount);
        Assert.Equal(2, snapshot.NextSequence);
        Assert.Equal(5U, snapshot.NextWirePosition);
        Assert.Equal(5L, snapshot.NextStreamPosition);
        Assert.Equal(
            PcpFmp4PublishingResult.Faulted,
            await boundary.PublishSegmentAsync(new byte[] { 2 }, PcpFmp4PublishingSegmentRole.Media));
    }

    [Fact]
    public async Task Cancellation_during_a_split_segment_is_terminal_and_does_not_count_the_blocked_packet()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new RecordingConsumer
        {
            BeforeAccept = async (call, token) =>
            {
                if (call == 3)
                {
                    entered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
            },
        };
        await using var boundary = CreateBoundary(
            consumer,
            new PcpFmp4PublishingBoundaryOptions
            {
                MaximumInitializationBytes = 4,
                MaximumMediaSegmentBytes = 16,
                MaximumPacketPayloadBytes = 4,
            });
        Assert.Equal(
            PcpFmp4PublishingResult.Published,
            await boundary.PublishSegmentAsync(new byte[] { 1 }, PcpFmp4PublishingSegmentRole.Initialization));
        using var cancellation = new CancellationTokenSource();
        var publishTask = boundary.PublishSegmentAsync(
            new byte[9],
            PcpFmp4PublishingSegmentRole.Media,
            cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        Assert.Equal(PcpFmp4PublishingResult.Cancelled, await publishTask);
        Assert.Equal(3, consumer.CallCount);
        Assert.Equal(2, consumer.Packets.Count);
        Assert.Equal(PcpFmp4PublishingBoundaryState.Faulted, boundary.Snapshot.State);
        Assert.Equal(PcpFmp4PublishingResult.Cancelled, boundary.Snapshot.Result);
        Assert.Equal(2, boundary.Snapshot.AcceptedPacketCount);
        Assert.Equal(5, boundary.Snapshot.AcceptedByteCount);
        Assert.Equal(0, boundary.Snapshot.MediaSegmentCount);
    }

    [Fact]
    public async Task Late_join_replays_the_complete_retained_head_before_future_data()
    {
        await using var fanout = new PcpRelayFanout(replayHeadForNewChildren: true);
        var create = PcpFmp4PublishingBoundary.Create(
            ChannelId,
            SessionId,
            fanout,
            out var created,
            new PcpFmp4PublishingBoundaryOptions
            {
                MaximumInitializationBytes = 4,
                MaximumMediaSegmentBytes = 16,
                MaximumPacketPayloadBytes = 4,
            });
        Assert.Equal(PcpFmp4PublishingBoundaryCreateResult.Created, create);
        await using var boundary = Assert.IsType<PcpFmp4PublishingBoundary>(created);
        byte[] initialization = [0, 1, 2, 3];
        Assert.Equal(
            PcpFmp4PublishingResult.Published,
            await boundary.PublishSegmentAsync(initialization, PcpFmp4PublishingSegmentRole.Initialization));

        var sink = new RecordingRelaySink();
        await using var child = fanout.AddChild(sink);
        await sink.WaitForCountAsync(1);
        byte[] media = [0x10, 0x11, 0x12, 0x13, 0x14];
        Assert.Equal(
            PcpFmp4PublishingResult.Published,
            await boundary.PublishSegmentAsync(media, PcpFmp4PublishingSegmentRole.Media));
        await sink.WaitForCountAsync(3);

        var packets = sink.Packets.ToArray();
        Assert.Equal(3, packets.Length);
        Assert.Equal(HeadPacketType, packets[0].Type);
        Assert.Equal(initialization, packets[0].Payload);
        Assert.Equal(DataPacketType, packets[1].Type);
        Assert.Equal(DataPacketType, packets[2].Type);
        Assert.Equal(media, packets.Skip(1).SelectMany(static packet => packet.Payload).ToArray());
        Assert.Equal([0L, 1L, 2L], packets.Select(static packet => packet.Sequence));
    }

    [Fact]
    public async Task Position_and_sequence_overflow_are_rejected_before_packet_delivery()
    {
        var options = new PcpFmp4PublishingBoundaryOptions
        {
            MaximumInitializationBytes = 4,
            MaximumMediaSegmentBytes = 16,
            MaximumPacketPayloadBytes = 4,
        };

        var wireConsumer = new RecordingConsumer();
        await using var wireBoundary = new PcpFmp4PublishingBoundary(
            ChannelId,
            SessionId,
            wireConsumer,
            options,
            uint.MaxValue - 1,
            (long)uint.MaxValue - 1,
            0);
        Assert.Equal(
            PcpFmp4PublishingResult.Published,
            await wireBoundary.PublishSegmentAsync(
                new byte[] { 1, 2, 3, 4 },
                PcpFmp4PublishingSegmentRole.Initialization));
        var wrappedPacket = Assert.Single(wireConsumer.Packets);
        Assert.Equal(uint.MaxValue - 1, wrappedPacket.WirePosition);
        Assert.Equal((long)uint.MaxValue - 1, wrappedPacket.StreamPosition);
        Assert.Equal(2u, wireBoundary.Snapshot.NextWirePosition);
        Assert.Equal((long)uint.MaxValue + 3, wireBoundary.Snapshot.NextStreamPosition);

        var streamConsumer = new RecordingConsumer();
        await using var streamBoundary = new PcpFmp4PublishingBoundary(
            ChannelId,
            SessionId,
            streamConsumer,
            options,
            0,
            long.MaxValue,
            0);
        Assert.Equal(
            PcpFmp4PublishingResult.PositionOverflow,
            await streamBoundary.PublishSegmentAsync(
                new byte[] { 1 },
                PcpFmp4PublishingSegmentRole.Initialization));
        Assert.Empty(streamConsumer.Packets);

        var sequenceConsumer = new RecordingConsumer();
        await using var sequenceBoundary = new PcpFmp4PublishingBoundary(
            ChannelId,
            SessionId,
            sequenceConsumer,
            options,
            0,
            0,
            long.MaxValue);
        Assert.Equal(
            PcpFmp4PublishingResult.PacketSequenceOverflow,
            await sequenceBoundary.PublishSegmentAsync(
                new byte[] { 1 },
                PcpFmp4PublishingSegmentRole.Initialization));
        Assert.Empty(sequenceConsumer.Packets);

        Assert.True(PcpFmp4PublishingBoundary.TryAdvancePositions(
            uint.MaxValue - 1,
            (long)uint.MaxValue - 1,
            4,
            out var wrappedWire,
            out var continuedStream));
        Assert.Equal(2u, wrappedWire);
        Assert.Equal((long)uint.MaxValue + 3, continuedStream);
        Assert.False(PcpFmp4PublishingBoundary.TryAdvancePositions(
            uint.MaxValue,
            long.MaxValue,
            1,
            out _,
            out _));
    }

    [Fact]
    public async Task Dispose_is_idempotent_and_prevents_later_publishing()
    {
        var consumer = new RecordingConsumer();
        var boundary = CreateBoundary(consumer);

        await boundary.DisposeAsync();
        await boundary.DisposeAsync();

        Assert.Equal(PcpFmp4PublishingBoundaryState.Disposed, boundary.Snapshot.State);
        Assert.Equal(PcpFmp4PublishingResult.Disposed, boundary.Snapshot.Result);
        Assert.Equal(
            PcpFmp4PublishingResult.Disposed,
            await boundary.PublishSegmentAsync(
                new byte[] { 1 },
                PcpFmp4PublishingSegmentRole.Initialization));
        Assert.Empty(consumer.Packets);
    }

    private static PcpFmp4PublishingBoundary CreateBoundary(
        RecordingConsumer consumer,
        PcpFmp4PublishingBoundaryOptions? options = null)
    {
        Assert.Equal(
            PcpFmp4PublishingBoundaryCreateResult.Created,
            PcpFmp4PublishingBoundary.Create(
                ChannelId,
                SessionId,
                consumer,
                out var boundary,
                options));
        return Assert.IsType<PcpFmp4PublishingBoundary>(boundary);
    }

    private sealed class RecordingConsumer : IPcpChannelPacketConsumer
    {
        public List<RecordedPacket> Packets { get; } = [];

        public int CallCount { get; private set; }

        public Func<int, CancellationToken, ValueTask>? BeforeAccept { get; init; }

        public async ValueTask ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (BeforeAccept is not null)
            {
                await BeforeAccept(CallCount, cancellationToken);
            }

            Packets.Add(new(
                packet.ChannelId,
                packet.SessionId,
                packet.Sequence,
                packet.Type,
                packet.WirePosition,
                packet.StreamPosition,
                packet.Payload.ToArray()));
        }
    }

    private sealed class RecordingRelaySink : IPcpRelayChildSink
    {
        private readonly ConcurrentQueue<RecordedRelayPacket> _packets = new();

        public IReadOnlyCollection<RecordedRelayPacket> Packets => _packets.ToArray();

        public ValueTask SendAsync(
            PcpRelayPacket packet,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _packets.Enqueue(new(
                packet.Sequence,
                packet.Type,
                packet.WirePosition,
                packet.StreamPosition,
                packet.Payload.ToArray()));
            return ValueTask.CompletedTask;
        }

        public async Task WaitForCountAsync(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (_packets.Count < count)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
            }
        }
    }

    private sealed record RecordedPacket(
        ChannelId ChannelId,
        ChannelSessionId SessionId,
        long Sequence,
        uint Type,
        uint WirePosition,
        long? StreamPosition,
        byte[] Payload);

    private sealed record RecordedRelayPacket(
        long Sequence,
        uint Type,
        uint WirePosition,
        long? StreamPosition,
        byte[] Payload);
}
