using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpRelayFanoutRecordingStreamSourceLeaseFactoryTests :
    IDisposable
{
    private readonly string outputDirectory = Path.Combine(
        Path.GetTempPath(),
        "PeerCast-Nova-PcpRecordingLeaseTests",
        Guid.NewGuid().ToString("N"));

    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact("531DC8DFC7FB42928AC2C0A626517A87", "N"));

    private static readonly ChannelSessionId SessionId = new(
        Guid.ParseExact("3F2504E04F8911D39A0C0305E82C3301", "N"));

    [Fact]
    public async Task Acquire_BeforeOwnerStartsOrAfterItCompletes_ReturnsNull()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(
            ChannelId,
            SessionId,
            fanout);

        Assert.Null(await factory.AcquireAsync(
            CreateRequest(),
            CancellationToken.None));

        factory.StartAccepting();
        await factory.CompleteAsync();

        Assert.Null(await factory.AcquireAsync(
            CreateRequest(),
            CancellationToken.None));
        Assert.Equal(0, factory.ActiveLeaseCount);
        Assert.Equal(0, fanout.ActiveChildCount);
    }

    [Fact]
    public async Task Acquire_PreCancelledDoesNotCreateReaderOrFanoutChild()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(
            ChannelId,
            SessionId,
            fanout);
        factory.StartAccepting();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await factory.AcquireAsync(
                CreateRequest(),
                cancellationSource.Token));

        Assert.Equal(0, factory.ActiveLeaseCount);
        Assert.Equal(0, fanout.ActiveChildCount);
    }

    [Fact]
    public async Task OwnerCompletionRaceWithAcquireReturnsOnlyAUsableLeaseOrNull()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(
            ChannelId,
            SessionId,
            fanout);
        factory.StartAccepting();
        var start = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var acquireTask = Task.Run(async () =>
        {
            await start.Task;
            return await factory.AcquireAsync(
                CreateRequest(),
                CancellationToken.None);
        });
        var completeTask = Task.Run(async () =>
        {
            await start.Task;
            await factory.CompleteAsync();
        });

        start.TrySetResult();
        await Task.WhenAll(acquireTask, completeTask);
        var lease = await acquireTask;

        if (lease is not null)
        {
            await using (lease)
            {
                Assert.Equal(-1, lease.ReadStream.ReadByte());
            }
        }

        Assert.Equal(0, factory.ActiveLeaseCount);
        Assert.Equal(0, fanout.ActiveChildCount);
    }

    [Fact]
    public async Task RegistryRemovalStopsNewAcquiresButExistingLeaseContinues()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(
            ChannelId,
            SessionId,
            fanout);
        var registry = new RecordingStreamSourceLeaseRegistry();
        registry.Upsert(ChannelId, factory);
        factory.StartAccepting();

        var lease = await registry.AcquireAsync(
            CreateRequest(),
            CancellationToken.None);
        Assert.NotNull(lease);

        Assert.True(registry.Remove(ChannelId));
        Assert.Null(await registry.AcquireAsync(
            CreateRequest(),
            CancellationToken.None));

        await fanout.ConsumeAsync(
            CreatePacket(0, [0x10, 0x20]),
            CancellationToken.None);
        Assert.True(await fanout.WaitForIdleAsync(
            TimeSpan.FromSeconds(2)));

        var buffer = new byte[2];
        await lease.ReadStream.ReadExactlyAsync(buffer);
        Assert.Equal([0x10, 0x20], buffer);

        await factory.CompleteAsync();
        Assert.Equal(-1, lease.ReadStream.ReadByte());
        await lease.DisposeAsync();

        Assert.Equal(0, factory.ActiveLeaseCount);
        await fanout.ConsumeAsync(
            CreatePacket(1, [0x30]),
            CancellationToken.None);
    }

    [Fact]
    public async Task NormalOwnerCompletionDrainsBufferedBytesThenReturnsEof()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(
            ChannelId,
            SessionId,
            fanout);
        factory.StartAccepting();
        var lease = await factory.AcquireAsync(
            CreateRequest(),
            CancellationToken.None);
        Assert.NotNull(lease);

        await fanout.ConsumeAsync(
            CreatePacket(7, [0x41, 0x42]),
            CancellationToken.None);
        Assert.True(await fanout.WaitForIdleAsync(
            TimeSpan.FromSeconds(2)));

        await factory.CompleteAsync();

        var buffer = new byte[2];
        await lease.ReadStream.ReadExactlyAsync(buffer);
        Assert.Equal([0x41, 0x42], buffer);
        Assert.Equal(-1, lease.ReadStream.ReadByte());
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task LateJoin_ReplayedHeadDoesNotRequireHistoricalSequenceContinuity()
    {
        await using var fanout = new PcpRelayFanout(
            replayHeadForNewChildren: true);
        var factory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(
            ChannelId,
            SessionId,
            fanout);
        factory.StartAccepting();

        await fanout.ConsumeAsync(
            CreatePacket(10, [0x10], type: 0x64616568),
            CancellationToken.None);
        await fanout.ConsumeAsync(
            CreatePacket(11, [0x11], type: 0x61746164),
            CancellationToken.None);

        var lease = await factory.AcquireAsync(
            CreateRequest(),
            CancellationToken.None);
        Assert.NotNull(lease);
        var readStream = lease.ReadStream;

        await fanout.ConsumeAsync(
            CreatePacket(12, [0x12], type: 0x61746164),
            CancellationToken.None);
        await fanout.ConsumeAsync(
            CreatePacket(13, [0x13], type: 0x61746164),
            CancellationToken.None);
        Assert.True(await fanout.WaitForIdleAsync(
            TimeSpan.FromSeconds(2)));
        await factory.CompleteAsync();

        using var recordedPayload = new MemoryStream();
        await readStream.CopyToAsync(recordedPayload);

        Assert.Equal([0x10, 0x12, 0x13], recordedPayload.ToArray());
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task LiveSourceSequenceGap_IsStillRejected()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(
            ChannelId,
            SessionId,
            fanout);
        factory.StartAccepting();
        var lease = await factory.AcquireAsync(
            CreateRequest(),
            CancellationToken.None);
        Assert.NotNull(lease);
        var readStream = lease.ReadStream;

        await fanout.ConsumeAsync(
            CreatePacket(20, [0x20]),
            CancellationToken.None);
        await fanout.ConsumeAsync(
            CreatePacket(22, [0x22]),
            CancellationToken.None);
        Assert.True(await fanout.WaitForIdleAsync(
            TimeSpan.FromSeconds(2)));

        using var recordedPayload = new MemoryStream();
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await readStream.CopyToAsync(recordedPayload));

        Assert.Contains("observed 22", exception.Message, StringComparison.Ordinal);
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task ExplicitDiscontinuity_ReachesRecordingReaderAndIsRejected()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(
            ChannelId,
            SessionId,
            fanout);
        factory.StartAccepting();
        var lease = await factory.AcquireAsync(
            CreateRequest(),
            CancellationToken.None);
        Assert.NotNull(lease);
        var readStream = lease.ReadStream;

        await fanout.ConsumeAsync(
            CreatePacket(
                0,
                [0x21],
                type: 0x61746164,
                discontinuity: new PcpStreamDiscontinuity(100, 50, 100)),
            CancellationToken.None);
        Assert.True(await fanout.WaitForIdleAsync(
            TimeSpan.FromSeconds(2)));

        using var recordedPayload = new MemoryStream();
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await readStream.CopyToAsync(recordedPayload));

        Assert.Contains(
            "discontinuity",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task RecordingRuntime_LateJoinFinalizesReplayHeadAndLiveBytes()
    {
        var headPayload = new byte[128 * 1024];
        Array.Fill(headPayload, (byte)0x10);
        var livePayload = new byte[128 * 1024];
        Array.Fill(livePayload, (byte)0x12);
        // The packet payloads in this late-join byte-copy test are opaque PCP
        // fixture bytes. FLV normalization is covered with valid FLV fixtures
        // by Nova.Core.Tests.
        var finalPath = Path.Combine(outputDirectory, "recording.bin");
        await using var fanout = new PcpRelayFanout(
            replayHeadForNewChildren: true);
        var factory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(
            ChannelId,
            SessionId,
            fanout);
        factory.StartAccepting();

        await fanout.ConsumeAsync(
            CreatePacket(10, headPayload, type: 0x64616568),
            CancellationToken.None);
        await fanout.ConsumeAsync(
            CreatePacket(11, [0x11], type: 0x61746164),
            CancellationToken.None);

        await using var runtime = new RecordingRuntime(
            new LeasedRecordingStreamSourceProvider(factory),
            new FixedPathProvider(finalPath));
        var started = await runtime.StartManualAsync(
            new RecordingCandidate(
                ChannelId,
                new ChannelMetadataSnapshot(
                    name: "Channel",
                    streamExtension: "bin")));

        Assert.Equal(RecordingManualStartResultKind.Started, started.Kind);
        await WaitForPartialSizeAsync(
            outputDirectory,
            expectedSize: headPayload.Length);

        await fanout.ConsumeAsync(
            CreatePacket(12, livePayload, type: 0x61746164),
            CancellationToken.None);
        Assert.True(await fanout.WaitForIdleAsync(
            TimeSpan.FromSeconds(2)));
        await WaitForPartialSizeAsync(
            outputDirectory,
            expectedSize: headPayload.Length + livePayload.Length);

        var stopped = await runtime.StopAsync(ChannelId);

        Assert.Equal(RecordingAutomationStopResultKind.Stopped, stopped.Kind);
        var expectedBytes = new byte[headPayload.Length + livePayload.Length];
        headPayload.CopyTo(expectedBytes, 0);
        livePayload.CopyTo(expectedBytes, headPayload.Length);
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(finalPath));
        Assert.Empty(Directory.GetFiles(outputDirectory, "*.partial"));
        await factory.CompleteAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task TerminalOwnerFaultIsPropagatedToReader()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(
            ChannelId,
            SessionId,
            fanout);
        factory.StartAccepting();
        var lease = await factory.AcquireAsync(
            CreateRequest(),
            CancellationToken.None);
        Assert.NotNull(lease);

        await fanout.ConsumeAsync(
            CreatePacket(0, [0x51]),
            CancellationToken.None);
        Assert.True(await fanout.WaitForIdleAsync(
            TimeSpan.FromSeconds(2)));

        await factory.CompleteAsync(
            new InvalidDataException("fixed terminal fault"));

        using var recordedPayload = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await lease.ReadStream.CopyToAsync(recordedPayload));
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task DisposingOneLeaseTwiceReleasesOnlyItsOwnFanoutChild()
    {
        await using var fanout = new PcpRelayFanout();
        var factory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(
            ChannelId,
            SessionId,
            fanout);
        factory.StartAccepting();
        var first = await factory.AcquireAsync(
            CreateRequest(),
            CancellationToken.None);
        var second = await factory.AcquireAsync(
            CreateRequest(),
            CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotNull(second);

        await first.DisposeAsync();
        await first.DisposeAsync();

        Assert.Equal(1, factory.ActiveLeaseCount);
        Assert.Equal(1, fanout.ActiveChildCount);

        await fanout.ConsumeAsync(
            CreatePacket(0, [0x61]),
            CancellationToken.None);
        Assert.True(await fanout.WaitForIdleAsync(
            TimeSpan.FromSeconds(2)));

        var payload = new byte[1];
        Assert.Equal(1, second.ReadStream.Read(payload));
        Assert.Equal(0x61, payload[0]);

        await factory.CompleteAsync();
        await second.DisposeAsync();
        Assert.Equal(0, fanout.ActiveChildCount);
    }

    [Fact]
    public async Task UnexpectedFanoutTerminationFaultsRecordingReader()
    {
        var fanout = new PcpRelayFanout();
        var factory = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(
            ChannelId,
            SessionId,
            fanout);
        factory.StartAccepting();
        var lease = await factory.AcquireAsync(
            CreateRequest(),
            CancellationToken.None);
        Assert.NotNull(lease);

        await fanout.DisposeAsync();

        using var recordedPayload = new MemoryStream();
        await Assert.ThrowsAsync<IOException>(
            async () => await lease.ReadStream.CopyToAsync(recordedPayload));
        await lease.DisposeAsync();
    }

    private static RecordingSinkStartRequest CreateRequest()
    {
        return new RecordingSinkStartRequest(
            new RecordingCandidate(
                ChannelId,
                new ChannelMetadataSnapshot(
                    name: "Channel",
                    streamExtension: "flv")),
            Guid.ParseExact("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "N"));
    }

    private static async Task WaitForPartialSizeAsync(
        string directory,
        int expectedSize)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(5));

        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();

            var partialPaths = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.partial")
                : [];

            if (partialPaths.Any(
                path => new FileInfo(path).Length >= expectedSize))
            {
                return;
            }

            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FixedPathProvider(string path) :
        IRecordingFilePathProvider
    {
        public string GetPath(
            RecordingSinkStartRequest request,
            RecordingSessionId sessionId)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(sessionId);
            return path;
        }
    }

    private static PcpChannelPacket CreatePacket(
        long sequence,
        byte[] payload,
        uint type = 1,
        PcpStreamDiscontinuity? discontinuity = null)
    {
        return new PcpChannelPacket(
            ChannelId,
            SessionId,
            sequence,
            type,
            wirePosition: checked((uint)(100 + sequence)),
            streamPosition: 100 + sequence,
            payload,
            discontinuity);
    }
}
