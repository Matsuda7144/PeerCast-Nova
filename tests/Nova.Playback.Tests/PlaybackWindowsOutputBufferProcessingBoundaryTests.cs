namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackWindowsOutputBufferProcessingBoundaryTests
{
    [Fact]
    public async Task ProcessesOneVideoFrameAndS16AudioBuffer()
    {
        using var setup = await SetupAsync();
        var native = new RecordingProcessor();
        using var boundary = Create(setup, native);

        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.Processed,
            await boundary.ProcessVideoAsync(Video(), default));
        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.Processed,
            await boundary.ProcessAudioAsync(Audio(), default));
        Assert.Equal([true], native.Starts);
    }

    [Fact]
    public async Task ProcessesFloatAudioAndStartsOnlyOnce()
    {
        using var setup = await SetupAsync(
            audioFormat: PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved);
        var native = new RecordingProcessor();
        using var boundary = Create(setup, native);
        var audio = Audio(PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved);

        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.Processed,
            await boundary.ProcessAudioAsync(audio, default));
        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.Processed,
            await boundary.ProcessAudioAsync(audio, default));
        Assert.Equal([true, false], native.Starts);
    }

    [Theory]
    [InlineData(PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved, 48_000, 2)]
    [InlineData(PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved, 44_100, 2)]
    [InlineData(PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved, 48_000, 1)]
    public async Task AudioConfigurationMismatchFailsBeforeNativeAccess(
        PlaybackDecodedAudioSampleFormat format,
        int sampleRate,
        int channelCount)
    {
        using var setup = await SetupAsync();
        var native = new RecordingProcessor();
        using var boundary = Create(setup, native);
        var bytesPerSample =
            format == PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved ? 2 : 4;
        var audio = new PlaybackWindowsAudioSampleBuffer(
            format,
            sampleRate,
            channelCount,
            2,
            new byte[2 * channelCount * bytesPerSample],
            11);

        Assert.Equal(
            PlaybackWindowsOutputBufferProcessingResult.InvalidConfiguration,
            await boundary.ProcessAudioAsync(audio, default));
        Assert.Empty(native.Starts);
    }

    [Fact]
    public async Task WouldBlockDoesNotConsumeStartTransition()
    {
        using var setup = await SetupAsync();
        var native = new RecordingProcessor
        {
            AudioResults = new Queue<PlaybackWindowsOutputNativeBufferResult>([
            PlaybackWindowsOutputNativeBufferResult.WouldBlock,
            PlaybackWindowsOutputNativeBufferResult.Processed])
        };
        using var boundary = Create(setup, native);

        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.WouldBlock,
            await boundary.ProcessAudioAsync(Audio(), default));
        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.Processed,
            await boundary.ProcessAudioAsync(Audio(), default));
        Assert.Equal([true, true], native.Starts);
    }

    [Fact]
    public async Task StrictLayoutAndGenerationValidationPrecedeNativeAccess()
    {
        using var setup = await SetupAsync();
        var native = new RecordingProcessor();
        using var boundary = Create(setup, native);

        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.InvalidConfiguration,
            await boundary.ProcessVideoAsync(Video() with { Stride = 7 }, default));
        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.InvalidConfiguration,
            await boundary.ProcessVideoAsync(Video() with { Buffer = new byte[15] }, default));
        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.InvalidConfiguration,
            await boundary.ProcessVideoAsync(Video() with { Generation = 8 }, default));
        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.InvalidConfiguration,
            await boundary.ProcessAudioAsync(Audio() with { Buffer = new byte[7] }, default));
        Assert.Equal(0, native.VideoCalls);
        Assert.Empty(native.Starts);
    }

    [Fact]
    public async Task UndefinedAndUnsupportedFormatsFailClosed()
    {
        using var setup = await SetupAsync();
        var native = new RecordingProcessor();
        using var boundary = Create(setup, native);

        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.Failure,
            await boundary.ProcessVideoAsync(Video() with
            { PixelFormat = (PlaybackDecodedVideoPixelFormat)99 }, default));
        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.Failure,
            await boundary.ProcessAudioAsync(Audio() with
            { SampleFormat = (PlaybackDecodedAudioSampleFormat)99 }, default));
    }

    [Theory]
    [InlineData(3,
        PlaybackWindowsOutputBufferProcessingResult.ResizeRequired)]
    [InlineData(4,
        PlaybackWindowsOutputBufferProcessingResult.DeviceLost)]
    [InlineData(2,
        PlaybackWindowsOutputBufferProcessingResult.FormatUnsupported)]
    public async Task NativeVideoResultsAreFixed(
        int nativeResult,
        PlaybackWindowsOutputBufferProcessingResult expected)
    {
        using var setup = await SetupAsync();
        using var boundary = Create(setup,
            new RecordingProcessor { VideoResult = (PlaybackWindowsOutputNativeBufferResult)nativeResult });
        Assert.Equal(expected, await boundary.ProcessVideoAsync(Video(), default));
    }

    [Fact]
    public async Task UndefinedNativeResultAndExceptionFailClosed()
    {
        using var first = await SetupAsync();
        using var second = await SetupAsync();
        using var undefined = Create(first, new RecordingProcessor
        { VideoResult = (PlaybackWindowsOutputNativeBufferResult)99 });
        using var throwing = Create(second, new RecordingProcessor { ThrowAudio = true });

        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.Failure,
            await undefined.ProcessVideoAsync(Video(), default));
        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.Failure,
            await throwing.ProcessAudioAsync(Audio(), default));
    }

    [Fact]
    public async Task MissingInitializationAndUnsupportedPlatformAvoidNativeAccess()
    {
        using var setup = await SetupAsync(initialize: false);
        var native = new RecordingProcessor();
        using var missing = Create(setup, native);
        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.InitializationRequired,
            await missing.ProcessVideoAsync(Video(), default));

        using var supportedSetup = await SetupAsync();
        using var unsupported = new PlaybackWindowsOutputBufferProcessingBoundary(
            supportedSetup.Activation, supportedSetup.Binding, supportedSetup.Initialization,
            native, static () => false);
        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.InvalidEnvironment,
            await unsupported.ProcessVideoAsync(Video(), default));
        Assert.Equal(0, native.VideoCalls);
    }

    [Fact]
    public async Task ResetRestartsAudioAndDisposeIsIdempotent()
    {
        using var setup = await SetupAsync();
        var native = new RecordingProcessor();
        var boundary = Create(setup, native);
        await boundary.ProcessAudioAsync(Audio(), default);
        Assert.Equal(PlaybackWindowsOutputBufferResetResult.Reset,
            await boundary.ResetAudioAsync(11, default));
        await boundary.ProcessAudioAsync(Audio(), default);
        boundary.Dispose();
        boundary.Dispose();

        Assert.Equal([true, true], native.Starts);
        Assert.Equal(["audio", "audio", "video"], native.Resets);
        Assert.Equal(PlaybackWindowsOutputBufferProcessingResult.Disposed,
            await boundary.ProcessVideoAsync(Video(), default));
    }

    [Fact]
    public async Task DisposeWaitsForInflightProcessing()
    {
        using var setup = await SetupAsync();
        var native = new RecordingProcessor { BlockVideo = true };
        var boundary = Create(setup, native);
        var process = Task.Run(async () => await boundary.ProcessVideoAsync(Video(), default));
        await native.Entered.Task;
        var dispose = Task.Run(boundary.Dispose);
        Assert.False(dispose.IsCompleted);
        native.Release.TrySetResult();
        await Task.WhenAll(process, dispose);
    }

    private static PlaybackWindowsOutputBufferProcessingBoundary Create(
        Setup setup, IPlaybackWindowsOutputNativeBufferProcessor native) =>
        new(setup.Activation, setup.Binding, setup.Initialization, native, static () => true);

    private static async Task<Setup> SetupAsync(
        bool initialize = true,
        PlaybackDecodedAudioSampleFormat audioFormat =
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved)
    {
        var activation = new PlaybackWindowsOutputActivationBoundary(
            new RecordingActivation(), static () => true);
        Assert.Equal(PlaybackWindowsOutputActivationResult.Activated, activation.Activate());
        var binding = new PlaybackWindowsOutputResourceBindingBoundary(
            activation, new RecordingBinding(), static () => true);
        Assert.Equal(PlaybackWindowsOutputBindingResult.Bound,
            await binding.ConfigureAsync(new(PlaybackDecodedVideoPixelFormat.Bgra32, 2, 2, 8, 7),
                new(audioFormat, 48_000, 2, 11), default));
        var initialization = new PlaybackWindowsOutputStreamInitializationBoundary(
            activation, binding, new RecordingInitializer(), static () => true);
        if (initialize)
            Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.Initialized,
                await initialization.InitializeAsync(
                    new(1, 2, 2, PlaybackDecodedVideoPixelFormat.Bgra32, 7),
                    new(audioFormat, 48_000, 2,
                        TimeSpan.FromMilliseconds(100), 11), default));
        return new(activation, binding, initialization);
    }

    private static PlaybackWindowsVideoFrameBuffer Video() =>
        new(PlaybackDecodedVideoPixelFormat.Bgra32, 2, 2, 8, new byte[16], 7);

    private static PlaybackWindowsAudioSampleBuffer Audio(
        PlaybackDecodedAudioSampleFormat format = PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved) =>
        new(format, 48_000, 2, 2, new byte[format ==
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved ? 8 : 16], 11);

    private sealed class Setup(
        PlaybackWindowsOutputActivationBoundary activation,
        PlaybackWindowsOutputResourceBindingBoundary binding,
        PlaybackWindowsOutputStreamInitializationBoundary initialization) : IDisposable
    {
        public PlaybackWindowsOutputActivationBoundary Activation { get; } = activation;
        public PlaybackWindowsOutputResourceBindingBoundary Binding { get; } = binding;
        public PlaybackWindowsOutputStreamInitializationBoundary Initialization { get; } = initialization;
        public void Dispose() { Initialization.Dispose(); Binding.Dispose(); Activation.Dispose(); }
    }

    private sealed class RecordingActivation : IPlaybackWindowsOutputNativeActivation
    {
        public PlaybackWindowsOutputActivationResult InitializeComApartment(out IDisposable? apartment) { apartment = new Lease(); return PlaybackWindowsOutputActivationResult.Activated; }
        public PlaybackWindowsOutputActivationResult ActivateDirect3D11(out IDisposable? device) { device = new Lease(); return PlaybackWindowsOutputActivationResult.Activated; }
        public PlaybackWindowsOutputActivationResult ActivateWasapiSharedEventDriven(out IDisposable? audio) { audio = new Lease(); return PlaybackWindowsOutputActivationResult.Activated; }
    }
    private sealed class Lease : IDisposable { public void Dispose() { } }
    private sealed class RecordingBinding : IPlaybackWindowsOutputNativeResourceBinding
    {
        public PlaybackWindowsOutputNativeBindingResult BindVideo(IDisposable resource, PlaybackVideoRendererConfiguration configuration) => PlaybackWindowsOutputNativeBindingResult.Bound;
        public PlaybackWindowsOutputNativeBindingResult BindAudio(IDisposable resource, PlaybackAudioDeviceConfiguration configuration) => PlaybackWindowsOutputNativeBindingResult.Bound;
        public void UnbindVideo() { }
        public void UnbindAudio() { }
    }
    private sealed class RecordingInitializer : IPlaybackWindowsOutputNativeStreamInitializer
    {
        public PlaybackWindowsOutputNativeStreamResult InitializeVideo(IDisposable resource, PlaybackWindowsVideoStreamConfiguration configuration) => PlaybackWindowsOutputNativeStreamResult.Initialized;
        public PlaybackWindowsOutputNativeStreamResult ResizeVideo(IDisposable resource, PlaybackWindowsVideoStreamConfiguration configuration) => PlaybackWindowsOutputNativeStreamResult.Initialized;
        public PlaybackWindowsOutputNativeStreamResult InitializeAudio(IDisposable resource, PlaybackWindowsAudioStreamConfiguration configuration) => PlaybackWindowsOutputNativeStreamResult.Initialized;
        public void ResetVideo(IDisposable resource) { }
        public void ResetAudio(IDisposable resource) { }
    }
    private sealed class RecordingProcessor : IPlaybackWindowsOutputNativeBufferProcessor
    {
        public PlaybackWindowsOutputNativeBufferResult VideoResult { get; init; } = PlaybackWindowsOutputNativeBufferResult.Processed;
        public Queue<PlaybackWindowsOutputNativeBufferResult>? AudioResults { get; init; }
        public bool ThrowAudio { get; init; }
        public bool BlockVideo { get; init; }
        public int VideoCalls { get; private set; }
        public List<bool> Starts { get; } = [];
        public List<string> Resets { get; } = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PlaybackWindowsOutputNativeBufferResult ProcessVideo(IDisposable resource, PlaybackWindowsVideoFrameBuffer frame)
        { VideoCalls++; Entered.TrySetResult(); if (BlockVideo) Release.Task.GetAwaiter().GetResult(); return VideoResult; }
        public PlaybackWindowsOutputNativeBufferResult ProcessAudio(IDisposable resource, PlaybackWindowsAudioSampleBuffer samples, bool startStream)
        { Starts.Add(startStream); if (ThrowAudio) throw new InvalidOperationException(); return AudioResults?.Dequeue() ?? PlaybackWindowsOutputNativeBufferResult.Processed; }
        public void ResetVideo(IDisposable resource) => Resets.Add("video");
        public void ResetAudio(IDisposable resource) => Resets.Add("audio");
    }
}
