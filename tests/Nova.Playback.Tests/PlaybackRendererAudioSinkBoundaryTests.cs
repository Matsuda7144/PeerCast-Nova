namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackRendererAudioSinkBoundaryTests
{
    [Fact]
    public async Task VideoConfigureAndSubmitPreserveCanonicalFrame()
    {
        var renderer = new RecordingVideoRenderer();
        using var boundary = new PlaybackVideoRendererBoundary(renderer);
        var frame = CreateVideoFrame();

        Assert.Equal(PlaybackOutputConfigureResult.Configured,
            await boundary.ConfigureAsync(VideoConfiguration(), TestContext.Current.CancellationToken));
        Assert.Equal(PlaybackOutputSubmitResult.Submitted,
            await boundary.SubmitAsync(frame, 7, TestContext.Current.CancellationToken));
        Assert.Equal(frame, renderer.LastFrame);
        Assert.Equal(7, renderer.LastGeneration);
        Assert.Equal(frame.Payload.ToArray(), renderer.PayloadSnapshot);
    }

    [Theory]
    [InlineData(PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved)]
    [InlineData(PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved)]
    public async Task AudioConfigureAndSubmitAcceptSupportedInterleavedFormats(
        PlaybackDecodedAudioSampleFormat format)
    {
        var sink = new RecordingAudioSink();
        using var boundary = new PlaybackAudioDeviceSinkBoundary(sink);
        var frame = CreateAudioFrame(format: format);

        Assert.Equal(PlaybackOutputConfigureResult.Configured,
            await boundary.ConfigureAsync(AudioConfiguration(format), TestContext.Current.CancellationToken));
        Assert.Equal(PlaybackOutputSubmitResult.Submitted,
            await boundary.SubmitAsync(frame, 11, TestContext.Current.CancellationToken));
        Assert.Equal(frame, sink.LastFrame);
        Assert.Equal(frame.Payload.ToArray(), sink.PayloadSnapshot);
    }

    [Fact]
    public async Task UndefinedAndOversizedConfigurationsFailClosed()
    {
        using var video = new PlaybackVideoRendererBoundary(new RecordingVideoRenderer());
        using var audio = new PlaybackAudioDeviceSinkBoundary(new RecordingAudioSink());

        Assert.Equal(PlaybackOutputConfigureResult.InvalidConfiguration,
            await video.ConfigureAsync(
                new((PlaybackDecodedVideoPixelFormat)99, 2, 2, 8, 0),
                TestContext.Current.CancellationToken));
        Assert.Equal(PlaybackOutputConfigureResult.InvalidConfiguration,
            await video.ConfigureAsync(
                new(PlaybackDecodedVideoPixelFormat.Bgra32, 16_384, 16_384, 65_536, 0),
                TestContext.Current.CancellationToken));
        Assert.Equal(PlaybackOutputConfigureResult.InvalidConfiguration,
            await audio.ConfigureAsync(
                new((PlaybackDecodedAudioSampleFormat)99, 48_000, 2, 0),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvalidFrameAndGenerationAreRejectedBeforeBackend()
    {
        var renderer = new RecordingVideoRenderer();
        using var boundary = new PlaybackVideoRendererBoundary(renderer);
        await boundary.ConfigureAsync(VideoConfiguration(), TestContext.Current.CancellationToken);

        Assert.Equal(PlaybackOutputSubmitResult.GenerationMismatch,
            await boundary.SubmitAsync(CreateVideoFrame(), 8, TestContext.Current.CancellationToken));
        Assert.Equal(PlaybackOutputSubmitResult.InvalidFrame,
            await boundary.SubmitAsync(default, 7, TestContext.Current.CancellationToken));
        Assert.Equal(0, renderer.SubmitCount);
    }

    [Theory]
    [InlineData(PlaybackOutputBackendResult.FormatUnsupported, PlaybackOutputSubmitResult.FormatUnsupported)]
    [InlineData(PlaybackOutputBackendResult.DeviceLost, PlaybackOutputSubmitResult.DeviceLost)]
    [InlineData(PlaybackOutputBackendResult.TemporarilyUnavailable, PlaybackOutputSubmitResult.TemporarilyUnavailable)]
    [InlineData(PlaybackOutputBackendResult.Rejected, PlaybackOutputSubmitResult.Rejected)]
    public async Task BackendSubmitStatesMapToFixedResults(
        PlaybackOutputBackendResult backendResult,
        PlaybackOutputSubmitResult expected)
    {
        var renderer = new RecordingVideoRenderer { SubmitResult = backendResult };
        using var boundary = new PlaybackVideoRendererBoundary(renderer);
        await boundary.ConfigureAsync(VideoConfiguration(), TestContext.Current.CancellationToken);

        Assert.Equal(expected,
            await boundary.SubmitAsync(CreateVideoFrame(), 7, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancellationAndBackendExceptionDoNotEscape()
    {
        var renderer = new RecordingVideoRenderer { WaitForCancellation = true };
        using var boundary = new PlaybackVideoRendererBoundary(renderer);
        await boundary.ConfigureAsync(VideoConfiguration(), TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Equal(PlaybackOutputSubmitResult.Cancelled,
            await boundary.SubmitAsync(CreateVideoFrame(), 7, cancellation.Token));

        renderer.WaitForCancellation = false;
        renderer.ThrowOnSubmit = true;
        Assert.Equal(PlaybackOutputSubmitResult.Failure,
            await boundary.SubmitAsync(CreateVideoFrame(), 7, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FlushAndResetAreExplicitAndResetRequiresReconfiguration()
    {
        using var boundary = new PlaybackAudioDeviceSinkBoundary(new RecordingAudioSink());
        await boundary.ConfigureAsync(AudioConfiguration(), TestContext.Current.CancellationToken);

        Assert.Equal(PlaybackOutputFlushResult.Flushed,
            await boundary.FlushAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PlaybackOutputResetResult.Reset,
            await boundary.ResetAsync(12, TestContext.Current.CancellationToken));
        Assert.Equal(PlaybackOutputSubmitResult.NotConfigured,
            await boundary.SubmitAsync(CreateAudioFrame(), 11, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposeAllowsActiveSubmitToFinishAndRejectsWaitingAndNewCalls()
    {
        var renderer = new RecordingVideoRenderer { BlockSubmits = true };
        using var boundary = new PlaybackVideoRendererBoundary(renderer);
        await boundary.ConfigureAsync(VideoConfiguration(), TestContext.Current.CancellationToken);
        var active = boundary.SubmitAsync(CreateVideoFrame(), 7, TestContext.Current.CancellationToken).AsTask();
        await renderer.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var waiting = boundary.SubmitAsync(CreateVideoFrame(2), 7, TestContext.Current.CancellationToken).AsTask();

        boundary.Dispose();
        renderer.Release.TrySetResult();

        Assert.Equal(PlaybackOutputSubmitResult.Submitted, await active);
        Assert.Equal(PlaybackOutputSubmitResult.Disposed, await waiting);
        Assert.Equal(PlaybackOutputSubmitResult.Disposed,
            await boundary.SubmitAsync(CreateVideoFrame(3), 7, TestContext.Current.CancellationToken));
        Assert.Equal(PlaybackOutputFlushResult.Disposed,
            await boundary.FlushAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, renderer.MaximumConcurrentSubmits);
    }

    [Fact]
    public async Task SameSinkSerializesSubmits()
    {
        var sink = new RecordingAudioSink { BlockSubmits = true };
        using var boundary = new PlaybackAudioDeviceSinkBoundary(sink);
        await boundary.ConfigureAsync(AudioConfiguration(), TestContext.Current.CancellationToken);
        var first = boundary.SubmitAsync(CreateAudioFrame(), 11, TestContext.Current.CancellationToken).AsTask();
        await sink.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = boundary.SubmitAsync(CreateAudioFrame(2), 11, TestContext.Current.CancellationToken).AsTask();

        Assert.False(second.IsCompleted);
        sink.Release.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, sink.MaximumConcurrentSubmits);
    }

    [Fact]
    public async Task VideoAndAudioUseIndependentAdmissionGates()
    {
        var renderer = new RecordingVideoRenderer { BlockSubmits = true };
        var sink = new RecordingAudioSink { BlockSubmits = true };
        using var video = new PlaybackVideoRendererBoundary(renderer);
        using var audio = new PlaybackAudioDeviceSinkBoundary(sink);
        await video.ConfigureAsync(VideoConfiguration(), TestContext.Current.CancellationToken);
        await audio.ConfigureAsync(AudioConfiguration(), TestContext.Current.CancellationToken);

        var videoSubmit = video.SubmitAsync(CreateVideoFrame(), 7, TestContext.Current.CancellationToken).AsTask();
        var audioSubmit = audio.SubmitAsync(CreateAudioFrame(), 11, TestContext.Current.CancellationToken).AsTask();
        await Task.WhenAll(
            renderer.Entered.Task.WaitAsync(TestContext.Current.CancellationToken),
            sink.Entered.Task.WaitAsync(TestContext.Current.CancellationToken));

        renderer.Release.TrySetResult();
        sink.Release.TrySetResult();
        await Task.WhenAll(videoSubmit, audioSubmit);
    }

    private static PlaybackVideoRendererConfiguration VideoConfiguration() =>
        new(PlaybackDecodedVideoPixelFormat.Bgra32, 2, 2, 8, 7);

    private static PlaybackAudioDeviceConfiguration AudioConfiguration(
        PlaybackDecodedAudioSampleFormat format = PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved) =>
        new(format, 48_000, 2, 11);

    private static class TestContext
    {
        public static TestState Current { get; } = new();
    }

    private sealed class TestState
    {
        public CancellationToken CancellationToken { get; } = CancellationToken.None;
    }

    private static PlaybackDecodedVideoFrame CreateVideoFrame(long sequence = 1) =>
        PlaybackDecodedVideoFrame.Create(
            sequence,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(20),
            PlaybackDecodedFrameDiscontinuityKind.None,
            PlaybackDecodedVideoPixelFormat.Bgra32,
            2,
            2,
            8,
            Enumerable.Range(0, 16).Select(static value => (byte)value).ToArray());

    private static PlaybackDecodedAudioFrame CreateAudioFrame(
        long sequence = 1,
        PlaybackDecodedAudioSampleFormat format = PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved)
    {
        var length = format == PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved ? 8 : 16;
        return PlaybackDecodedAudioFrame.Create(
            sequence,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(20),
            PlaybackDecodedFrameDiscontinuityKind.None,
            format,
            48_000,
            2,
            2,
            Enumerable.Range(0, length).Select(static value => (byte)value).ToArray());
    }

    private sealed class RecordingVideoRenderer : IPlaybackVideoRenderer
    {
        private int _active;
        public PlaybackOutputBackendResult SubmitResult { get; init; } = PlaybackOutputBackendResult.Success;
        public bool ThrowOnSubmit { get; set; }
        public bool WaitForCancellation { get; set; }
        public bool BlockSubmits { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PlaybackDecodedVideoFrame LastFrame { get; private set; }
        public byte[] PayloadSnapshot { get; private set; } = [];
        public long LastGeneration { get; private set; }
        public int SubmitCount { get; private set; }
        public int MaximumConcurrentSubmits { get; private set; }

        public ValueTask<PlaybackOutputBackendResult> ConfigureAsync(PlaybackVideoRendererConfiguration configuration, CancellationToken cancellationToken) => ValueTask.FromResult(PlaybackOutputBackendResult.Success);
        public ValueTask<PlaybackOutputBackendResult> FlushAsync(CancellationToken cancellationToken) => ValueTask.FromResult(PlaybackOutputBackendResult.Success);
        public ValueTask<PlaybackOutputBackendResult> ResetAsync(long generation, CancellationToken cancellationToken) => ValueTask.FromResult(PlaybackOutputBackendResult.Success);

        public async ValueTask<PlaybackOutputBackendResult> SubmitAsync(PlaybackDecodedVideoFrame frame, long generation, CancellationToken cancellationToken)
        {
            SubmitCount++;
            var active = Interlocked.Increment(ref _active);
            MaximumConcurrentSubmits = Math.Max(MaximumConcurrentSubmits, active);
            try
            {
                LastFrame = frame;
                LastGeneration = generation;
                PayloadSnapshot = frame.Payload.ToArray();
                Entered.TrySetResult();
                if (WaitForCancellation)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                if (BlockSubmits)
                {
                    await Release.Task.WaitAsync(cancellationToken);
                }
                if (ThrowOnSubmit)
                {
                    throw new InvalidOperationException("fixed-test-exception");
                }
                return SubmitResult;
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class RecordingAudioSink : IPlaybackAudioDeviceSink
    {
        private int _active;
        public bool BlockSubmits { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PlaybackDecodedAudioFrame LastFrame { get; private set; }
        public byte[] PayloadSnapshot { get; private set; } = [];
        public int MaximumConcurrentSubmits { get; private set; }

        public ValueTask<PlaybackOutputBackendResult> ConfigureAsync(PlaybackAudioDeviceConfiguration configuration, CancellationToken cancellationToken) => ValueTask.FromResult(PlaybackOutputBackendResult.Success);
        public ValueTask<PlaybackOutputBackendResult> FlushAsync(CancellationToken cancellationToken) => ValueTask.FromResult(PlaybackOutputBackendResult.Success);
        public ValueTask<PlaybackOutputBackendResult> ResetAsync(long generation, CancellationToken cancellationToken) => ValueTask.FromResult(PlaybackOutputBackendResult.Success);

        public async ValueTask<PlaybackOutputBackendResult> SubmitAsync(PlaybackDecodedAudioFrame frame, long generation, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            MaximumConcurrentSubmits = Math.Max(MaximumConcurrentSubmits, active);
            try
            {
                LastFrame = frame;
                PayloadSnapshot = frame.Payload.ToArray();
                Entered.TrySetResult();
                if (BlockSubmits)
                {
                    await Release.Task.WaitAsync(cancellationToken);
                }
                return PlaybackOutputBackendResult.Success;
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }
}
