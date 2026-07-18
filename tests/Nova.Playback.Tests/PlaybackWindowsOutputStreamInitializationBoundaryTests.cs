namespace PeerCast.Nova.Playback.Tests;

#pragma warning disable CA1861

public sealed class PlaybackWindowsOutputStreamInitializationBoundaryTests
{
    [Theory]
    [InlineData(PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved)]
    [InlineData(PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved)]
    public async Task BoundResourcesInitializeVideoAndAudio(
        PlaybackDecodedAudioSampleFormat format)
    {
        using var setup = await SetupAsync();
        var native = new RecordingInitializer();
        using var boundary = Create(setup, native);

        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.Initialized,
            await boundary.InitializeAsync(Video(7), Audio(11, format), default));
        Assert.Equal(new(PlaybackWindowsOutputStreamState.Initialized,
            PlaybackWindowsOutputStreamState.Initialized, 7, 11), boundary.Snapshot);
        Assert.Empty(setup.ActivationNative.Released);
    }

    [Fact]
    public async Task ActivationAndBindingAreRequiredBeforeNativeAccess()
    {
        var activationNative = new RecordingActivation();
        using var activation = new PlaybackWindowsOutputActivationBoundary(
            activationNative, static () => true);
        using var binding = new PlaybackWindowsOutputResourceBindingBoundary(
            activation, new RecordingBinding(), static () => true);
        var native = new RecordingInitializer();
        using var boundary = new PlaybackWindowsOutputStreamInitializationBoundary(
            activation, binding, native, static () => true);

        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.ActivationRequired,
            await boundary.InitializeAsync(Video(7), Audio(11), default));
        Assert.Equal(0, native.VideoCalls);
    }

    [Fact]
    public async Task ActivatedButUnboundResourcesReturnBindingRequired()
    {
        var activationNative = new RecordingActivation();
        using var activation = new PlaybackWindowsOutputActivationBoundary(
            activationNative, static () => true);
        Assert.Equal(PlaybackWindowsOutputActivationResult.Activated,
            activation.Activate());
        using var binding = new PlaybackWindowsOutputResourceBindingBoundary(
            activation, new RecordingBinding(), static () => true);
        var native = new RecordingInitializer();
        using var boundary = new PlaybackWindowsOutputStreamInitializationBoundary(
            activation, binding, native, static () => true);

        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.BindingRequired,
            await boundary.InitializeAsync(Video(7), Audio(11), default));
        Assert.Equal(0, native.VideoCalls);
    }

    [Fact]
    public async Task UnsupportedPlatformDoesNotAccessNative()
    {
        using var setup = await SetupAsync();
        var native = new RecordingInitializer();
        using var boundary = new PlaybackWindowsOutputStreamInitializationBoundary(
            setup.Activation, setup.Binding, native, static () => false);

        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.PlatformUnsupported,
            await boundary.InitializeAsync(Video(7), Audio(11), default));
        Assert.Equal(0, native.VideoCalls);
    }

    [Fact]
    public async Task InvalidWindowDurationAndFormatsFailClosed()
    {
        using var setup = await SetupAsync();
        var native = new RecordingInitializer();
        using var boundary = Create(setup, native);

        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.InvalidConfiguration,
            await boundary.InitializeAsync(Video(7) with { WindowToken = 0 }, Audio(11), default));
        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.InvalidConfiguration,
            await boundary.InitializeAsync(Video(7), Audio(11) with { BufferDuration = TimeSpan.Zero }, default));
        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.Failure,
            await boundary.InitializeAsync(Video(7) with { PixelFormat = (PlaybackDecodedVideoPixelFormat)99 }, Audio(11), default));
        Assert.Equal(0, native.VideoCalls);
    }

    [Fact]
    public async Task AudioPartialFailureRollsBackAudioThenVideo()
    {
        using var setup = await SetupAsync();
        var native = new RecordingInitializer { AudioResult = PlaybackWindowsOutputNativeStreamResult.Failure };
        using var boundary = Create(setup, native);

        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.Failure,
            await boundary.InitializeAsync(Video(7), Audio(11), default));
        Assert.Equal(new[] { "audio", "video" }, native.ResetOrder);
    }

    [Fact]
    public async Task ResizeAndGenerationMismatchHaveFixedResults()
    {
        using var setup = await SetupAsync();
        var native = new RecordingInitializer();
        using var boundary = Create(setup, native);
        await boundary.InitializeAsync(Video(7), Audio(11), default);

        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.Initialized,
            await boundary.ResizeVideoAsync(Video(7) with { Width = 4 }, default));
        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.ResizeRequired,
            await boundary.ResizeVideoAsync(Video(8), default));
    }

    [Fact]
    public async Task DeviceLostAndResetUpdateIndependentGeneration()
    {
        using var setup = await SetupAsync();
        var native = new RecordingInitializer { AudioResult = PlaybackWindowsOutputNativeStreamResult.DeviceLost };
        using var boundary = Create(setup, native);

        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.DeviceLost,
            await boundary.InitializeAsync(Video(7), Audio(11), default));
        Assert.Equal(PlaybackWindowsOutputStreamResetResult.DeviceLost,
            await boundary.ResetAudioAsync(12, default));
        Assert.Equal(12, boundary.Snapshot.AudioGeneration);
        Assert.Equal(PlaybackWindowsOutputStreamResetResult.DeviceLost,
            await boundary.ResetVideoAsync(8, default));
        Assert.Equal(8, boundary.Snapshot.VideoGeneration);
    }

    [Fact]
    public async Task DuplicateInitializeAndRepeatedResetAreFixed()
    {
        using var setup = await SetupAsync();
        using var boundary = Create(setup, new RecordingInitializer());
        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.Initialized,
            await boundary.InitializeAsync(Video(7), Audio(11), default));
        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.AlreadyInitialized,
            await boundary.InitializeAsync(Video(7), Audio(11), default));
        Assert.Equal(PlaybackWindowsOutputStreamResetResult.Reset,
            await boundary.ResetVideoAsync(8, default));
        Assert.Equal(PlaybackWindowsOutputStreamResetResult.NotInitialized,
            await boundary.ResetVideoAsync(9, default));
    }

    [Fact]
    public async Task UndefinedResultAndExceptionFailClosed()
    {
        using var setup1 = await SetupAsync();
        using var setup2 = await SetupAsync();
        using var undefined = Create(setup1, new RecordingInitializer
        { VideoResult = (PlaybackWindowsOutputNativeStreamResult)99 });
        using var throwing = Create(setup2, new RecordingInitializer { ThrowOnAudio = true });

        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.Failure,
            await undefined.InitializeAsync(Video(7), Audio(11), default));
        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.Failure,
            await throwing.InitializeAsync(Video(7), Audio(11), default));
    }

    [Fact]
    public async Task DisposeWaitsForInflightAndIsIdempotent()
    {
        using var setup = await SetupAsync();
        var native = new RecordingInitializer { BlockVideo = true };
        var boundary = Create(setup, native);
        var initialize = Task.Run(async () => await boundary.InitializeAsync(Video(7), Audio(11), default));
        await native.Entered.Task;
        var dispose = Task.Run(boundary.Dispose);
        Assert.False(dispose.IsCompleted);
        native.Release.TrySetResult();
        await Task.WhenAll(initialize, dispose);
        boundary.Dispose();

        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.Disposed,
            await boundary.InitializeAsync(Video(7), Audio(11), default));
        Assert.Equal(PlaybackWindowsOutputStreamResetResult.Disposed,
            await boundary.ResetAudioAsync(12, default));
    }

    [Fact]
    public async Task MissingActivationResourceFailsWithoutInitializerAccess()
    {
        var activationNative = new RecordingActivation { OmitAudio = true };
        using var activation = new PlaybackWindowsOutputActivationBoundary(activationNative, static () => true);
        Assert.Equal(PlaybackWindowsOutputActivationResult.Failure, activation.Activate());
        using var binding = new PlaybackWindowsOutputResourceBindingBoundary(activation, new RecordingBinding(), static () => true);
        var native = new RecordingInitializer();
        using var boundary = new PlaybackWindowsOutputStreamInitializationBoundary(activation, binding, native, static () => true);

        Assert.Equal(PlaybackWindowsOutputStreamInitializationResult.ActivationRequired,
            await boundary.InitializeAsync(Video(7), Audio(11), default));
        Assert.Equal(0, native.VideoCalls);
    }

    private static PlaybackWindowsOutputStreamInitializationBoundary Create(Setup setup, IPlaybackWindowsOutputNativeStreamInitializer native) =>
        new(setup.Activation, setup.Binding, native, static () => true);

    private static async Task<Setup> SetupAsync()
    {
        var activationNative = new RecordingActivation();
        var activation = new PlaybackWindowsOutputActivationBoundary(activationNative, static () => true);
        Assert.Equal(PlaybackWindowsOutputActivationResult.Activated, activation.Activate());
        var binding = new PlaybackWindowsOutputResourceBindingBoundary(activation, new RecordingBinding(), static () => true);
        Assert.Equal(PlaybackWindowsOutputBindingResult.Bound,
            await binding.ConfigureAsync(
                new(PlaybackDecodedVideoPixelFormat.Bgra32, 2, 2, 8, 7),
                new(PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved, 48_000, 2, 11), default));
        return new(activationNative, activation, binding);
    }

    private static PlaybackWindowsVideoStreamConfiguration Video(long generation) =>
        new(1, 2, 2, PlaybackDecodedVideoPixelFormat.Bgra32, generation);
    private static PlaybackWindowsAudioStreamConfiguration Audio(long generation,
        PlaybackDecodedAudioSampleFormat format = PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved) =>
        new(format, 48_000, 2, TimeSpan.FromMilliseconds(100), generation);

    private sealed class Setup(RecordingActivation activationNative, PlaybackWindowsOutputActivationBoundary activation, PlaybackWindowsOutputResourceBindingBoundary binding) : IDisposable
    {
        public RecordingActivation ActivationNative { get; } = activationNative;
        public PlaybackWindowsOutputActivationBoundary Activation { get; } = activation;
        public PlaybackWindowsOutputResourceBindingBoundary Binding { get; } = binding;
        public void Dispose() { Binding.Dispose(); Activation.Dispose(); }
    }

    private sealed class RecordingActivation : IPlaybackWindowsOutputNativeActivation
    {
        public bool OmitAudio { get; init; }
        public List<string> Released { get; } = [];
        public PlaybackWindowsOutputActivationResult InitializeComApartment(out IDisposable? apartment) { apartment = new Lease("com", Released); return PlaybackWindowsOutputActivationResult.Activated; }
        public PlaybackWindowsOutputActivationResult ActivateDirect3D11(out IDisposable? device) { device = new Lease("video", Released); return PlaybackWindowsOutputActivationResult.Activated; }
        public PlaybackWindowsOutputActivationResult ActivateWasapiSharedEventDriven(out IDisposable? audio) { audio = OmitAudio ? null : new Lease("audio", Released); return PlaybackWindowsOutputActivationResult.Activated; }
    }
    private sealed class Lease(string name, List<string> released) : IDisposable { public void Dispose() => released.Add(name); }
    private sealed class RecordingBinding : IPlaybackWindowsOutputNativeResourceBinding
    {
        public PlaybackWindowsOutputNativeBindingResult BindVideo(IDisposable borrowedResource, PlaybackVideoRendererConfiguration configuration) => PlaybackWindowsOutputNativeBindingResult.Bound;
        public PlaybackWindowsOutputNativeBindingResult BindAudio(IDisposable borrowedResource, PlaybackAudioDeviceConfiguration configuration) => PlaybackWindowsOutputNativeBindingResult.Bound;
        public void UnbindVideo() { }
        public void UnbindAudio() { }
    }
    private sealed class RecordingInitializer : IPlaybackWindowsOutputNativeStreamInitializer
    {
        public PlaybackWindowsOutputNativeStreamResult VideoResult { get; init; } = PlaybackWindowsOutputNativeStreamResult.Initialized;
        public PlaybackWindowsOutputNativeStreamResult AudioResult { get; init; } = PlaybackWindowsOutputNativeStreamResult.Initialized;
        public bool ThrowOnAudio { get; init; }
        public bool BlockVideo { get; init; }
        public int VideoCalls { get; private set; }
        public List<string> ResetOrder { get; } = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PlaybackWindowsOutputNativeStreamResult InitializeVideo(IDisposable borrowedResource, PlaybackWindowsVideoStreamConfiguration configuration)
        { VideoCalls++; Entered.TrySetResult(); if (BlockVideo) Release.Task.GetAwaiter().GetResult(); return VideoResult; }
        public PlaybackWindowsOutputNativeStreamResult ResizeVideo(IDisposable borrowedResource, PlaybackWindowsVideoStreamConfiguration configuration) => VideoResult;
        public PlaybackWindowsOutputNativeStreamResult InitializeAudio(IDisposable borrowedResource, PlaybackWindowsAudioStreamConfiguration configuration)
        { if (ThrowOnAudio) throw new InvalidOperationException("fixed-test-exception"); return AudioResult; }
        public void ResetVideo(IDisposable borrowedResource) => ResetOrder.Add("video");
        public void ResetAudio(IDisposable borrowedResource) => ResetOrder.Add("audio");
    }
}
