using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PcpPlaybackLatencyBufferTests
{
    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact("531DC8DFC7FB42928AC2C0A626517A87", "N"));

    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact("3F2504E04F8911D39A0C0305E82C3301", "N"));

    [Fact]
    public async Task Consume_CopiesPayloadBeforeCallbackReturns()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        byte[] source = [0x10, 0x20, 0x30];

        await buffer.ConsumeAsync(
            CreatePacket(0, source),
            CancellationToken.None);
        source.AsSpan().Fill(0xFF);

        using var lease = await buffer.ReadAsync();
        Assert.NotNull(lease);
        Assert.Equal([0x10, 0x20, 0x30], lease.Payload.ToArray());
        Assert.Equal(0, lease.Sequence);
        Assert.Equal(100, lease.StreamPosition);
    }

    [Fact]
    public async Task PacketLimit_DropsOldestAndMarksFirstRetainedPacket()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer(
            new PlaybackLatencyBufferOptions
            {
                MaxPendingPackets = 2,
                MaxPendingBytes = 64,
            });

        await ConsumeAsync(buffer, 0, [0x00]);
        await ConsumeAsync(buffer, 1, [0x01]);
        await ConsumeAsync(buffer, 2, [0x02]);

        using var first = await buffer.ReadAsync();
        using var second = await buffer.ReadAsync();
        Assert.NotNull(first);
        Assert.NotNull(second);

        Assert.Equal(1, first.Sequence);
        Assert.Equal(
            PlaybackDiscontinuityKind.LatencySkip,
            first.Discontinuity);
        Assert.Equal(2, second.Sequence);
        Assert.Equal(
            PlaybackDiscontinuityKind.None,
            second.Discontinuity);
        Assert.Equal(1, buffer.Snapshot.SkippedPacketCount);
        Assert.Equal(1, buffer.Snapshot.SkippedByteCount);
    }

    [Fact]
    public async Task ByteLimit_DropsEnoughOldestPacketsForLatestInput()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer(
            new PlaybackLatencyBufferOptions
            {
                MaxPendingPackets = 3,
                MaxPendingBytes = 4,
            });

        await ConsumeAsync(buffer, 0, [0x00, 0x00]);
        await ConsumeAsync(buffer, 1, [0x01, 0x01]);
        await ConsumeAsync(buffer, 2, [0x02, 0x02, 0x02]);

        using var lease = await buffer.ReadAsync();
        Assert.NotNull(lease);
        Assert.Equal(2, lease.Sequence);
        Assert.Equal(
            PlaybackDiscontinuityKind.LatencySkip,
            lease.Discontinuity);
        Assert.Equal(2, buffer.Snapshot.SkippedPacketCount);
        Assert.Equal(4, buffer.Snapshot.SkippedByteCount);
    }

    [Fact]
    public async Task OversizedPacket_IsSkippedAndMarksNextAcceptedPacket()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer(
            new PlaybackLatencyBufferOptions
            {
                MaxPendingPackets = 2,
                MaxPendingBytes = 2,
            });

        await ConsumeAsync(buffer, 0, [0x00, 0x01, 0x02]);
        await ConsumeAsync(buffer, 1, [0x03]);

        using var lease = await buffer.ReadAsync();
        Assert.NotNull(lease);
        Assert.Equal(1, lease.Sequence);
        Assert.Equal(
            PlaybackDiscontinuityKind.LatencySkip,
            lease.Discontinuity);
        Assert.Equal(2, buffer.Snapshot.ReceivedPacketCount);
        Assert.Equal(4, buffer.Snapshot.ReceivedByteCount);
        Assert.Equal(1, buffer.Snapshot.SkippedPacketCount);
        Assert.Equal(3, buffer.Snapshot.SkippedByteCount);
    }

    [Fact]
    public async Task SourceDiscontinuity_IsMappedWithoutSourceDetails()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();

        await buffer.ConsumeAsync(
            CreatePacket(0, [0x10], sourceDiscontinuity: true),
            CancellationToken.None);

        using var lease = await buffer.ReadAsync();
        Assert.NotNull(lease);
        Assert.Equal(
            PlaybackDiscontinuityKind.Source,
            lease.Discontinuity);
        Assert.Equal(1, buffer.Snapshot.DiscontinuityPacketCount);
    }

    [Fact]
    public async Task Complete_DrainsPendingPacketsThenEndsReader()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        await ConsumeAsync(buffer, 0, [0x10]);

        buffer.Complete();

        Assert.Equal(PlaybackInputState.Draining, buffer.Snapshot.State);
        using var lease = await buffer.ReadAsync();
        Assert.NotNull(lease);
        Assert.Null(await buffer.ReadAsync());
        Assert.Equal(PlaybackInputState.Completed, buffer.Snapshot.State);
        Assert.Equal(
            PlaybackInputEndReason.SourceCompleted,
            buffer.Snapshot.EndReason);
    }

    [Fact]
    public async Task Stop_WakesWaitingReaderAndDiscardsPendingPackets()
    {
        await using var waitingBuffer = new PcpPlaybackLatencyBuffer();
        var readTask = waitingBuffer.ReadAsync().AsTask();

        waitingBuffer.Stop();

        Assert.Null(await readTask.WaitAsync(TimeSpan.FromSeconds(2)));

        await using var pendingBuffer = new PcpPlaybackLatencyBuffer();
        await ConsumeAsync(pendingBuffer, 0, [0x10, 0x20]);
        pendingBuffer.Stop();

        var snapshot = pendingBuffer.Snapshot;
        Assert.Equal(PlaybackInputState.Stopped, snapshot.State);
        Assert.Equal(PlaybackInputEndReason.OwnerStopped, snapshot.EndReason);
        Assert.Equal(0, snapshot.PendingPacketCount);
        Assert.Equal(0, snapshot.PendingByteCount);
        Assert.Equal(1, snapshot.SkippedPacketCount);
        Assert.Equal(2, snapshot.SkippedByteCount);
    }

    [Fact]
    public async Task CanceledRead_DoesNotStopBuffer()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => buffer.ReadAsync(cancellationSource.Token).AsTask());

        await ConsumeAsync(buffer, 0, [0x10]);
        using var lease = await buffer.ReadAsync();
        Assert.NotNull(lease);
        Assert.Equal([0x10], lease.Payload.ToArray());
        Assert.Equal(PlaybackInputState.Accepting, buffer.Snapshot.State);
    }

    [Fact]
    public async Task ConcurrentRead_IsRejectedWithoutStoppingOwner()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        var firstRead = buffer.ReadAsync().AsTask();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => buffer.ReadAsync().AsTask());

        buffer.Stop();
        Assert.Null(await firstRead.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Lease_DisposeInvalidatesPayloadAndReleasesOutstandingCount()
    {
        await using var buffer = new PcpPlaybackLatencyBuffer();
        await ConsumeAsync(buffer, 0, [0x10, 0x20]);
        var lease = await buffer.ReadAsync();
        Assert.NotNull(lease);

        Assert.Equal(1, buffer.Snapshot.OutstandingPacketCount);
        Assert.Equal(2, buffer.Snapshot.OutstandingByteCount);

        buffer.Stop();
        Assert.Equal(1, buffer.Snapshot.OutstandingPacketCount);
        Assert.Equal([0x10, 0x20], lease.Payload.ToArray());

        lease.Dispose();
        Assert.Equal(0, buffer.Snapshot.OutstandingPacketCount);
        Assert.Equal(0, buffer.Snapshot.OutstandingByteCount);
        Assert.Throws<ObjectDisposedException>(
            () =>
            {
                _ = lease.Payload;
            });
    }

    [Fact]
    public async Task Consume_AfterCompletionOrStopIsRejected()
    {
        await using var completed = new PcpPlaybackLatencyBuffer();
        completed.Complete();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => completed.ConsumeAsync(
                CreatePacket(0, [0x10]),
                CancellationToken.None).AsTask());

        await using var stopped = new PcpPlaybackLatencyBuffer();
        stopped.Stop();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => stopped.ConsumeAsync(
                CreatePacket(0, [0x10]),
                CancellationToken.None).AsTask());
    }

    [Fact]
    public void Options_RejectNonFiniteOrExcessiveBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackLatencyBufferOptions
            {
                MaxPendingPackets = 0,
            }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackLatencyBufferOptions
            {
                MaxPendingPackets =
                    PlaybackLatencyBufferOptions.MaximumPendingPacketsLimit + 1,
            }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackLatencyBufferOptions
            {
                MaxPendingBytes = 0,
            }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackLatencyBufferOptions
            {
                MaxPendingBytes =
                    PlaybackLatencyBufferOptions.MaximumPendingBytesLimit + 1,
            }.Validate());
    }

    [Fact]
    public void Status_ContainsOnlyFixedEnumsBooleansAndNumbers()
    {
        var allowedTypes = new HashSet<Type>
        {
            typeof(int),
            typeof(long),
            typeof(PlaybackInputState),
            typeof(PlaybackInputEndReason?),
        };

        var unexpected = typeof(PlaybackInputStatusSnapshot)
            .GetProperties()
            .Where(property => !allowedTypes.Contains(property.PropertyType))
            .Select(property => property.Name)
            .ToArray();

        Assert.Empty(unexpected);
    }

    private static ValueTask ConsumeAsync(
        PcpPlaybackLatencyBuffer buffer,
        long sequence,
        byte[] payload)
    {
        return buffer.ConsumeAsync(
            CreatePacket(sequence, payload),
            CancellationToken.None);
    }

    private static PcpChannelPacket CreatePacket(
        long sequence,
        byte[] payload,
        bool sourceDiscontinuity = false)
    {
        return new PcpChannelPacket(
            ChannelId,
            SessionId,
            sequence,
            type: 1,
            wirePosition: checked((uint)(100 + sequence)),
            streamPosition: 100 + sequence,
            payload,
            sourceDiscontinuity
                ? new PcpStreamDiscontinuity(99, 50, 99)
                : null);
    }
}
