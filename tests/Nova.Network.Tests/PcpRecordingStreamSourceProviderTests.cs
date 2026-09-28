using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpRecordingStreamSourceProviderTests
{
    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));

    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact(
            "3F2504E04F8911D39A0C0305E82C3301",
            "N"));

    [Fact]
    public async Task OpenRead_NaturalPumpCompletionProducesOneByteStream()
    {
        var pump = new PacketPump(
            [
                [0x01, 0x02],
                [0x03, 0x04],
            ]);
        var provider =
            new PcpRecordingStreamSourceProvider(pump);

        await using var stream = await provider.OpenReadAsync(
            CreateRequest(),
            CancellationToken.None);

        using var destination = new MemoryStream();
        await stream.CopyToAsync(destination);

        Assert.Equal(
            [0x01, 0x02, 0x03, 0x04],
            destination.ToArray());
        Assert.Equal(1, pump.RunCount);
    }

    [Fact]
    public async Task OpenRead_PumpFailurePropagatesThroughStreamRead()
    {
        var provider =
            new PcpRecordingStreamSourceProvider(
                new ThrowingPump());

        await using var stream = await provider.OpenReadAsync(
            CreateRequest(),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () =>
            {
                using var destination = new MemoryStream();
                await stream.CopyToAsync(destination);
            });

        Assert.Contains(
            "synthetic pump failure",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenRead_DisposeCancelsActivePump()
    {
        var pump = new BlockingPump();
        var provider =
            new PcpRecordingStreamSourceProvider(pump);

        var stream = await provider.OpenReadAsync(
            CreateRequest(),
            CancellationToken.None);

        await pump.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        await stream.DisposeAsync();

        await pump.Canceled.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task OpenRead_PreCancelledDoesNotStartPump()
    {
        var pump = new PacketPump(
            [[0x01]]);
        var provider =
            new PcpRecordingStreamSourceProvider(pump);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await provider.OpenReadAsync(
                CreateRequest(),
                source.Token));

        Assert.Equal(0, pump.RunCount);
    }

    [Fact]
    public async Task OpenRead_InvalidPumpSequenceFaultsStream()
    {
        var provider =
            new PcpRecordingStreamSourceProvider(
                new InvalidSequencePump());

        await using var stream = await provider.OpenReadAsync(
            CreateRequest(),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(
            async () =>
            {
                using var destination = new MemoryStream();
                await stream.CopyToAsync(destination);
            });
    }

    private static RecordingSinkStartRequest CreateRequest()
    {
        return new RecordingSinkStartRequest(
            new RecordingCandidate(
                ChannelId,
                new ChannelMetadataSnapshot(
                    name: "Channel",
                    streamExtension: "flv")),
            Guid.ParseExact(
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                "N"));
    }

    private static PcpChannelPacket CreatePacket(
        long sequence,
        byte[] payload)
    {
        return new PcpChannelPacket(
            ChannelId,
            SessionId,
            sequence,
            type: 1,
            wirePosition: checked((uint)(100 + sequence)),
            streamPosition: 100 + sequence,
            payload,
            discontinuity: null);
    }

    private sealed class PacketPump :
        IPcpRecordingPacketPump
    {
        private readonly IReadOnlyList<byte[]> payloads;
        private int runCount;

        public PacketPump(
            IReadOnlyList<byte[]> payloads)
        {
            this.payloads = payloads;
        }

        public int RunCount =>
            Volatile.Read(ref runCount);

        public async Task RunAsync(
            RecordingSinkStartRequest request,
            IPcpChannelPacketConsumer consumer,
            CancellationToken cancellationToken)
        {
            _ = request;
            Interlocked.Increment(ref runCount);

            for (var index = 0; index < payloads.Count; index++)
            {
                await consumer.ConsumeAsync(
                    CreatePacket(
                        index,
                        payloads[index]),
                    cancellationToken);
            }
        }
    }

    private sealed class ThrowingPump :
        IPcpRecordingPacketPump
    {
        public Task RunAsync(
            RecordingSinkStartRequest request,
            IPcpChannelPacketConsumer consumer,
            CancellationToken cancellationToken)
        {
            _ = request;
            _ = consumer;
            cancellationToken.ThrowIfCancellationRequested();

            throw new InvalidDataException(
                "synthetic pump failure");
        }
    }

    private sealed class InvalidSequencePump :
        IPcpRecordingPacketPump
    {
        public async Task RunAsync(
            RecordingSinkStartRequest request,
            IPcpChannelPacketConsumer consumer,
            CancellationToken cancellationToken)
        {
            _ = request;

            await consumer.ConsumeAsync(
                CreatePacket(
                    sequence: 1,
                    [0x01]),
                cancellationToken);
        }
    }

    private sealed class BlockingPump :
        IPcpRecordingPacketPump
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Canceled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunAsync(
            RecordingSinkStartRequest request,
            IPcpChannelPacketConsumer consumer,
            CancellationToken cancellationToken)
        {
            _ = request;
            _ = consumer;
            Entered.TrySetResult();

            try
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                Canceled.TrySetResult();
                throw;
            }
        }
    }
}
