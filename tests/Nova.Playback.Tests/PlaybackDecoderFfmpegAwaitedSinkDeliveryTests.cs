namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderFfmpegAwaitedSinkDeliveryTests
{
    private static readonly string[] RequiredFrameSymbols =
        ["av_frame_alloc", "av_frame_free", "avcodec_receive_frame"];

    [Fact]
    public async Task DeliverAsync_VideoAwaitsSinkAndClearsPayloadAfterCompletion()
    {
        using var owners = new TestOwners();
        using var input = BgraFrame();
        using var boundary = new PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary();
        var sink = new BlockingSink();

        var delivery = boundary.DeliverAsync(
            owners.Context,
            input,
            17,
            PlaybackDecodedFrameDiscontinuityKind.DecoderReset,
            sink,
            CancellationToken.None).AsTask();
        await sink.Entered;

        Assert.False(delivery.IsCompleted);
        Assert.Equal(17, sink.Video.Sequence);
        Assert.Equal(PlaybackDecodedFrameDiscontinuityKind.DecoderReset, sink.Video.Discontinuity);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, sink.Video.Payload.ToArray());

        sink.Release();
        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered, await delivery);
        Assert.All(sink.Video.Payload.ToArray(), value => Assert.Equal(0, value));
        Assert.False(input.IsDisposed);
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public async Task DeliverAsync_AudioForwardsCanonicalFrameAndToken()
    {
        using var owners = new TestOwners();
        using var input = PackedAudioFrame();
        using var boundary = new PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary();
        using var cancellation = new CancellationTokenSource();
        var sink = new RecordingSink();

        var result = await boundary.DeliverAsync(
            owners.Context,
            input,
            4,
            PlaybackDecodedFrameDiscontinuityKind.None,
            sink,
            cancellation.Token);

        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered, result);
        Assert.Equal(cancellation.Token, sink.Token);
        Assert.Equal(4, sink.Audio.Sequence);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, sink.Snapshot);
        Assert.All(sink.Audio.Payload.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task DeliverAsync_UsesManagedVideoConversionBoundary()
    {
        using var owners = new TestOwners();
        using var input = YuvFrame();
        using var boundary = new PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary();
        var sink = new RecordingSink();

        var result = await boundary.DeliverAsync(
            owners.Context,
            input,
            1,
            PlaybackDecodedFrameDiscontinuityKind.None,
            sink,
            CancellationToken.None);

        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered, result);
        Assert.Equal(PlaybackDecodedVideoPixelFormat.Bgra32, sink.Video.PixelFormat);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, sink.Snapshot);
    }

    [Fact]
    public async Task DeliverAsync_UsesManagedPlanarAudioConversionBoundary()
    {
        using var owners = new TestOwners();
        using var input = PlanarAudioFrame();
        using var boundary = new PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary();
        var sink = new RecordingSink();

        var result = await boundary.DeliverAsync(
            owners.Context,
            input,
            2,
            PlaybackDecodedFrameDiscontinuityKind.None,
            sink,
            CancellationToken.None);

        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered, result);
        Assert.Equal(new byte[] { 1, 2, 5, 6, 3, 4, 7, 8 }, sink.Snapshot);
    }

    [Fact]
    public async Task DeliverAsync_CancellationIsFixedAndPreservesContext()
    {
        using var owners = new TestOwners();
        using var input = BgraFrame();
        using var boundary = new PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary();
        using var cancellation = new CancellationTokenSource();
        var sink = new CancellingSink();

        var delivery = boundary.DeliverAsync(
            owners.Context,
            input,
            1,
            PlaybackDecodedFrameDiscontinuityKind.None,
            sink,
            cancellation.Token).AsTask();
        await sink.Entered;
        cancellation.Cancel();

        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Cancelled, await delivery);
        Assert.False(owners.Context.IsDisposed);
        Assert.All(sink.Payload.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task DeliverAsync_SinkExceptionIsFixedAndPreservesContext()
    {
        using var owners = new TestOwners();
        using var input = BgraFrame();
        using var boundary = new PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary();
        var sink = new ThrowingSink();

        var result = await boundary.DeliverAsync(
            owners.Context,
            input,
            1,
            PlaybackDecodedFrameDiscontinuityKind.None,
            sink,
            CancellationToken.None);

        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.SinkFailed, result);
        Assert.False(owners.Context.IsDisposed);
        Assert.All(sink.Payload.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task DeliverAsync_UnexpectedIntegrationFailureDisposesContext()
    {
        using var owners = new TestOwners();
        using var input = YuvFrame();
        var allocator = new TrackingAllocator();
        using var boundary = new PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary(
            new PlaybackDecoderFfmpegDecodedOutputAdapter(),
            new PlaybackDecoderFfmpegManagedFormatConversionBoundary(
                allocator,
                new UnexpectedWorker()));

        var result = await boundary.DeliverAsync(
            owners.Context,
            input,
            1,
            PlaybackDecodedFrameDiscontinuityKind.None,
            new RecordingSink(),
            CancellationToken.None);

        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.IntegrationFailed, result);
        Assert.True(owners.Context.IsDisposed);
        Assert.All(Assert.IsType<byte[]>(allocator.Buffer), value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task DeliverAsync_SerializesCallbacksAndPreservesInvocationOrder()
    {
        using var owners = new TestOwners();
        using var first = BgraFrame();
        using var second = PackedAudioFrame();
        using var boundary = new PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary();
        var sink = new SerialSink();

        var firstDelivery = boundary.DeliverAsync(
            owners.Context,
            first,
            1,
            PlaybackDecodedFrameDiscontinuityKind.None,
            sink,
            CancellationToken.None).AsTask();
        await sink.FirstEntered;
        var secondDelivery = boundary.DeliverAsync(
            owners.Context,
            second,
            2,
            PlaybackDecodedFrameDiscontinuityKind.None,
            sink,
            CancellationToken.None).AsTask();

        Assert.False(secondDelivery.IsCompleted);
        Assert.Equal(1, sink.MaximumConcurrency);
        sink.ReleaseFirst();

        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered, await firstDelivery);
        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered, await secondDelivery);
        Assert.Equal(new long[] { 1, 2 }, sink.Sequences);
        Assert.Equal(1, sink.MaximumConcurrency);
    }

    [Fact]
    public async Task Dispose_DuringCallbackAllowsSafeCompletionAndPayloadClear()
    {
        using var owners = new TestOwners();
        using var input = BgraFrame();
        using var boundary = new PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary();
        var sink = new BlockingSink();

        var delivery = boundary.DeliverAsync(
            owners.Context,
            input,
            5,
            PlaybackDecodedFrameDiscontinuityKind.None,
            sink,
            CancellationToken.None).AsTask();
        await sink.Entered;
        boundary.Dispose();
        sink.Release();

        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered, await delivery);
        Assert.All(sink.Video.Payload.ToArray(), value => Assert.Equal(0, value));
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public async Task Dispose_WithActiveAndWaitingDeliveryReturnsFixedWaiterResult()
    {
        using var owners = new TestOwners();
        using var first = BgraFrame();
        using var second = PackedAudioFrame();
        using var boundary = new PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary();
        var sink = new SerialSink();

        var active = boundary.DeliverAsync(
            owners.Context,
            first,
            1,
            PlaybackDecodedFrameDiscontinuityKind.None,
            sink,
            CancellationToken.None).AsTask();
        await sink.FirstEntered;
        var waiting = boundary.DeliverAsync(
            owners.Context,
            second,
            2,
            PlaybackDecodedFrameDiscontinuityKind.None,
            sink,
            CancellationToken.None).AsTask();
        boundary.Dispose();
        sink.ReleaseFirst();

        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered, await active);
        Assert.Equal(
            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.IntegrationFailed,
            await waiting);
        Assert.Equal(new long[] { 1 }, sink.Sequences);
        Assert.Equal(1, sink.MaximumConcurrency);
        Assert.False(owners.Context.IsDisposed);
    }

    [Fact]
    public async Task Dispose_NewDeliveryReturnsFixedResultWithoutSinkOrContextMutation()
    {
        using var owners = new TestOwners();
        using var input = BgraFrame();
        using var boundary = new PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary();
        var sink = new RecordingSink();
        boundary.Dispose();

        var result = await boundary.DeliverAsync(
            owners.Context,
            input,
            1,
            PlaybackDecodedFrameDiscontinuityKind.None,
            sink,
            CancellationToken.None);

        Assert.Equal(PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.IntegrationFailed, result);
        Assert.Empty(sink.Snapshot);
        Assert.False(owners.Context.IsDisposed);
    }

    private static PlaybackDecoderFfmpegManagedFrame BgraFrame() => new(
        VideoMetadata(PlaybackDecoderFfmpegFrameFormat.VideoBgra32, 2, 1),
        [new(0, 8, 1, 8, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })]);

    private static PlaybackDecoderFfmpegManagedFrame YuvFrame() => new(
        VideoMetadata(PlaybackDecoderFfmpegFrameFormat.VideoYuv420P, 1, 1),
        [new(0, 1, 1, 1, new byte[] { 16 }), new(1, 1, 1, 1, new byte[] { 128 }),
            new(2, 1, 1, 1, new byte[] { 128 })]);

    private static PlaybackDecoderFfmpegManagedFrame PackedAudioFrame() => new(
        AudioMetadata(PlaybackDecoderFfmpegFrameFormat.AudioSigned16),
        [new(0, 8, 1, 8, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })]);

    private static PlaybackDecoderFfmpegManagedFrame PlanarAudioFrame() => new(
        AudioMetadata(PlaybackDecoderFfmpegFrameFormat.AudioSigned16Planar),
        [new(0, 4, 1, 4, new byte[] { 1, 2, 3, 4 }),
            new(1, 4, 1, 4, new byte[] { 5, 6, 7, 8 })]);

    private static PlaybackDecoderFfmpegFrameMetadata VideoMetadata(
        PlaybackDecoderFfmpegFrameFormat format,
        int width,
        int height) => new(
            PlaybackDecoderFfmpegFrameMediaKind.Video,
            format,
            width,
            height,
            0,
            0,
            0,
            default,
            default,
            default);

    private static PlaybackDecoderFfmpegFrameMetadata AudioMetadata(
        PlaybackDecoderFfmpegFrameFormat format) => new(
            PlaybackDecoderFfmpegFrameMediaKind.Audio,
            format,
            0,
            0,
            48_000,
            2,
            2,
            default,
            default,
            default);

    private sealed class RecordingSink : IPlaybackDecodedFrameSink
    {
        public PlaybackDecodedAudioFrame Audio { get; private set; }
        public PlaybackDecodedVideoFrame Video { get; private set; }
        public byte[] Snapshot { get; private set; } = [];
        public CancellationToken Token { get; private set; }

        public ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken)
        {
            Audio = frame;
            Snapshot = frame.Payload.ToArray();
            Token = cancellationToken;
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken)
        {
            Video = frame;
            Snapshot = frame.Payload.ToArray();
            Token = cancellationToken;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingSink : IPlaybackDecodedFrameSink
    {
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;
        public PlaybackDecodedVideoFrame Video { get; private set; }

        public ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken) => throw new InvalidOperationException();

        public async ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken)
        {
            Video = frame;
            _entered.SetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        public void Release() => _release.SetResult();
    }

    private sealed class CancellingSink : IPlaybackDecodedFrameSink
    {
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;
        public ReadOnlyMemory<byte> Payload { get; private set; }

        public ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken) => throw new InvalidOperationException();

        public async ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken)
        {
            Payload = frame.Payload;
            _entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class ThrowingSink : IPlaybackDecodedFrameSink
    {
        public ReadOnlyMemory<byte> Payload { get; private set; }

        public ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken) => throw new InvalidOperationException();

        public ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken)
        {
            Payload = frame.Payload;
            throw new InvalidOperationException("sensitive");
        }
    }

    private sealed class SerialSink : IPlaybackDecodedFrameSink
    {
        private readonly TaskCompletionSource _firstEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirst = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<long> _sequences = [];
        private int _concurrency;

        public Task FirstEntered => _firstEntered.Task;
        public IReadOnlyList<long> Sequences => _sequences;
        public int MaximumConcurrency { get; private set; }

        public async ValueTask WriteAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken) =>
            await RecordAsync(frame.Sequence, cancellationToken);

        public async ValueTask WriteVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken) =>
            await RecordAsync(frame.Sequence, cancellationToken);

        public void ReleaseFirst() => _releaseFirst.SetResult();

        private async ValueTask RecordAsync(long sequence, CancellationToken cancellationToken)
        {
            var concurrency = Interlocked.Increment(ref _concurrency);
            MaximumConcurrency = Math.Max(MaximumConcurrency, concurrency);
            _sequences.Add(sequence);
            try
            {
                if (sequence == 1)
                {
                    _firstEntered.SetResult();
                    await _releaseFirst.Task.WaitAsync(cancellationToken);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _concurrency);
            }
        }
    }

    private sealed class UnexpectedWorker : IPlaybackDecoderManagedFormatConversionWorker
    {
        public void Convert(
            PlaybackDecoderFfmpegManagedFrame frame,
            Span<byte> destination) => throw new NotSupportedException();
    }

    private sealed class TrackingAllocator : IPlaybackDecoderManagedBufferAllocator
    {
        public byte[]? Buffer { get; private set; }

        public byte[] Allocate(int length)
        {
            Buffer = Enumerable.Repeat((byte)123, length).ToArray();
            return Buffer;
        }
    }

    private sealed class TestOwners : IDisposable
    {
        private readonly string _directory;
        private readonly VerifiedPlaybackDecoderRuntimeLease _runtime;

        public TestOwners()
        {
            _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, "runtime.bin");
            File.WriteAllBytes(path, [1]);
            _runtime = new VerifiedPlaybackDecoderRuntimeLease(
                new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runtime.bin"] = new(path, FileMode.Open, FileAccess.Read, FileShare.Read),
                },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runtime.bin"] = path,
                },
                []);
            var symbols = RequiredFrameSymbols
                .Select((name, index) => (name, pointer: (nint)(index + 1)))
                .ToDictionary(item => item.name, item => item.pointer, StringComparer.Ordinal);
            var gate = new PlaybackDecoderFfmpegAbiSymbolGateLease(
                _runtime,
                new FakeLoader(),
                [(nint)100],
                symbols);
            Context = new PlaybackDecoderFfmpegContextLease(
                _runtime,
                gate,
                new FakeContextNativeApi(),
                PlaybackDecoderFfmpegCodecId.H264,
                (nint)200,
                (nint)300);
            Context.MarkParametersTransferred();
            Context.MarkOpened();
        }

        public PlaybackDecoderFfmpegContextLease Context { get; }

        public void Dispose()
        {
            Context.Dispose();
            _runtime.Dispose();
            Directory.Delete(_directory, true);
        }
    }

    private sealed class FakeLoader : IPlaybackDecoderNativeLibraryLoader
    {
        public nint Load(string verifiedPath, PlaybackDecoderNativeSearchScope searchScope) => 0;
        public nint GetSymbol(nint module, string symbol) => 0;
        public uint InvokeVersion(nint symbol) => 0;
        public void Free(nint module) { }
    }

    private sealed class FakeContextNativeApi : IPlaybackDecoderFfmpegContextNativeApi
    {
        public nint FindDecoder(nint function, PlaybackDecoderFfmpegCodecId codecId) => 0;
        public nint AllocateContext(nint function, nint decoder) => 0;
        public void FreeContext(nint function, ref nint context) => context = 0;
    }
}
