namespace PeerCast.Nova.Playback.Tests;

#pragma warning disable CA1861 // Inline cleanup sequences are clearer in each assertion.

public sealed class PlaybackWindowsOutputResourceBindingBoundaryTests
{
    [Theory]
    [InlineData(PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved)]
    [InlineData(PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved)]
    public async Task ActivatedResourcesBindVideoAndAudioWithoutOwnershipTransfer(
        PlaybackDecodedAudioSampleFormat format)
    {
        var activationNative = new RecordingActivation();
        using var activation = Activated(activationNative);
        var native = new RecordingBinding();
        using var boundary = Create(activation, native);

        Assert.Equal(PlaybackWindowsOutputBindingResult.Bound,
            await boundary.ConfigureAsync(Video(7), Audio(11, format), default));
        Assert.Equal(new(PlaybackWindowsOutputBindingState.Bound,
            PlaybackWindowsOutputBindingState.Bound, 7, 11), boundary.Snapshot);
        Assert.Empty(activationNative.Released);

        boundary.Dispose();
        Assert.Empty(activationNative.Released);
        activation.Dispose();
        Assert.Equal(new[] { "audio-resource", "video-resource", "com" },
            activationNative.Released);
    }

    [Fact]
    public async Task ActivationRequiredDoesNotAccessBindingNative()
    {
        using var activation = new PlaybackWindowsOutputActivationBoundary(
            new RecordingActivation(), static () => true);
        var native = new RecordingBinding();
        using var boundary = Create(activation, native);

        Assert.Equal(PlaybackWindowsOutputBindingResult.ActivationRequired,
            await boundary.ConfigureAsync(Video(1), Audio(1), default));
        Assert.Equal(0, native.VideoCalls);
    }

    [Fact]
    public async Task UnsupportedPlatformDoesNotBorrowOrAccessNative()
    {
        var activationNative = new RecordingActivation();
        using var activation = Activated(activationNative);
        var native = new RecordingBinding();
        using var boundary = new PlaybackWindowsOutputResourceBindingBoundary(
            activation, native, static () => false);

        Assert.Equal(PlaybackWindowsOutputBindingResult.PlatformUnsupported,
            await boundary.ConfigureAsync(Video(1), Audio(1), default));
        Assert.Equal(0, native.VideoCalls);
    }

    [Fact]
    public async Task OwningThreadMismatchIsInvalidEnvironmentWithoutNativeBinding()
    {
        using var activation = new PlaybackWindowsOutputActivationBoundary(
            new RecordingActivation(), static () => true,
            enforceOwningThread: true);
        Assert.Equal(PlaybackWindowsOutputActivationResult.Activated,
            activation.Activate());
        var native = new RecordingBinding();
        using var boundary = Create(activation, native);

        var result = await Task.Run(async () =>
            await boundary.ConfigureAsync(Video(1), Audio(1), default));

        Assert.Equal(PlaybackWindowsOutputBindingResult.InvalidEnvironment, result);
        Assert.Equal(0, native.VideoCalls);
    }

    [Fact]
    public async Task UnsupportedAndUndefinedFormatsFailBeforeNativeAccess()
    {
        using var activation = Activated(new RecordingActivation());
        var native = new RecordingBinding();
        using var boundary = Create(activation, native);

        Assert.Equal(PlaybackWindowsOutputBindingResult.FormatUnsupported,
            await boundary.ConfigureAsync(
                Video(1) with { PixelFormat = (PlaybackDecodedVideoPixelFormat)99 },
                Audio(1), default));
        Assert.Equal(PlaybackWindowsOutputBindingResult.FormatUnsupported,
            await boundary.ConfigureAsync(Video(1),
                Audio(1) with { SampleFormat = (PlaybackDecodedAudioSampleFormat)99 },
                default));
        Assert.Equal(0, native.VideoCalls);
    }

    [Fact]
    public async Task AudioFailureRollsBackAudioThenVideo()
    {
        using var activation = Activated(new RecordingActivation());
        var native = new RecordingBinding
        {
            AudioResult = PlaybackWindowsOutputNativeBindingResult.Failure,
        };
        using var boundary = Create(activation, native);

        Assert.Equal(PlaybackWindowsOutputBindingResult.Failure,
            await boundary.ConfigureAsync(Video(1), Audio(1), default));
        Assert.Equal(new[] { "audio", "video" }, native.Unbound);
        Assert.Equal(PlaybackWindowsOutputBindingState.Unbound,
            boundary.Snapshot.VideoState);
    }

    [Fact]
    public async Task ResetUnbindsInReverseOrderAndUpdatesGeneration()
    {
        using var activation = Activated(new RecordingActivation());
        var native = new RecordingBinding();
        using var boundary = Create(activation, native);
        await boundary.ConfigureAsync(Video(3), Audio(4), default);

        Assert.Equal(PlaybackWindowsOutputBindingResetResult.Reset,
            await boundary.ResetAsync(9, default));
        Assert.Equal(new[] { "audio", "video" }, native.Unbound);
        Assert.Equal(new(PlaybackWindowsOutputBindingState.Unbound,
            PlaybackWindowsOutputBindingState.Unbound, 9, 9), boundary.Snapshot);
        Assert.Equal(PlaybackWindowsOutputBindingResetResult.NotBound,
            await boundary.ResetAsync(10, default));
    }

    [Fact]
    public async Task DuplicateConfigureIsRejectedWithoutNativeReentry()
    {
        using var activation = Activated(new RecordingActivation());
        var native = new RecordingBinding();
        using var boundary = Create(activation, native);

        Assert.Equal(PlaybackWindowsOutputBindingResult.Bound,
            await boundary.ConfigureAsync(Video(1), Audio(1), default));
        Assert.Equal(PlaybackWindowsOutputBindingResult.AlreadyBound,
            await boundary.ConfigureAsync(Video(2), Audio(2), default));
        Assert.Equal(1, native.VideoCalls);
    }

    [Fact]
    public async Task DeviceLostIsFixedAndResetClearsBindings()
    {
        using var activation = Activated(new RecordingActivation());
        var native = new RecordingBinding
        {
            AudioResult = PlaybackWindowsOutputNativeBindingResult.DeviceLost,
        };
        using var boundary = Create(activation, native);

        Assert.Equal(PlaybackWindowsOutputBindingResult.DeviceLost,
            await boundary.ConfigureAsync(Video(1), Audio(1), default));
        Assert.Equal(PlaybackWindowsOutputBindingState.DeviceLost,
            boundary.Snapshot.VideoState);
        Assert.Equal(PlaybackWindowsOutputBindingResetResult.DeviceLost,
            await boundary.ResetAsync(2, default));
    }

    [Fact]
    public async Task UndefinedResultAndExceptionFailClosed()
    {
        using var activation1 = Activated(new RecordingActivation());
        using var activation2 = Activated(new RecordingActivation());
        using var undefined = Create(activation1, new RecordingBinding
        {
            VideoResult = (PlaybackWindowsOutputNativeBindingResult)99,
        });
        using var throwing = Create(activation2, new RecordingBinding
        {
            ThrowOnAudio = true,
        });

        Assert.Equal(PlaybackWindowsOutputBindingResult.Failure,
            await undefined.ConfigureAsync(Video(1), Audio(1), default));
        Assert.Equal(PlaybackWindowsOutputBindingResult.Failure,
            await throwing.ConfigureAsync(Video(1), Audio(1), default));
    }

    [Fact]
    public async Task DisposeWaitsForInflightBindingAndIsIdempotent()
    {
        using var activation = Activated(new RecordingActivation());
        var native = new RecordingBinding { BlockVideo = true };
        var boundary = Create(activation, native);
        var configure = Task.Run(async () =>
            await boundary.ConfigureAsync(Video(1), Audio(1), default));
        await native.Entered.Task;
        var dispose = Task.Run(boundary.Dispose);

        Assert.False(dispose.IsCompleted);
        native.Release.TrySetResult();
        await Task.WhenAll(configure, dispose);
        boundary.Dispose();

        Assert.Equal(PlaybackWindowsOutputBindingResult.Disposed,
            await boundary.ConfigureAsync(Video(2), Audio(2), default));
        Assert.Equal(PlaybackWindowsOutputBindingResetResult.Disposed,
            await boundary.ResetAsync(2, default));
        Assert.Equal(PlaybackWindowsOutputBindingState.Disposed,
            boundary.Snapshot.VideoState);
    }

    [Fact]
    public async Task MissingActivatedResourceFailsBeforeBinding()
    {
        var activationNative = new RecordingActivation { OmitAudio = true };
        using var activation = new PlaybackWindowsOutputActivationBoundary(
            activationNative, static () => true);
        Assert.Equal(PlaybackWindowsOutputActivationResult.Failure,
            activation.Activate());
        var native = new RecordingBinding();
        using var boundary = Create(activation, native);

        Assert.Equal(PlaybackWindowsOutputBindingResult.ActivationRequired,
            await boundary.ConfigureAsync(Video(1), Audio(1), default));
        Assert.Equal(0, native.VideoCalls);
    }

    private static PlaybackWindowsOutputResourceBindingBoundary Create(
        PlaybackWindowsOutputActivationBoundary activation,
        IPlaybackWindowsOutputNativeResourceBinding native) =>
        new(activation, native, static () => true);

    private static PlaybackWindowsOutputActivationBoundary Activated(
        RecordingActivation native)
    {
        var activation = new PlaybackWindowsOutputActivationBoundary(native,
            static () => true);
        Assert.Equal(PlaybackWindowsOutputActivationResult.Activated,
            activation.Activate());
        return activation;
    }

    private static PlaybackVideoRendererConfiguration Video(long generation) =>
        new(PlaybackDecodedVideoPixelFormat.Bgra32, 2, 2, 8, generation);

    private static PlaybackAudioDeviceConfiguration Audio(long generation,
        PlaybackDecodedAudioSampleFormat format =
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved) =>
        new(format, 48_000, 2, generation);

    private sealed class RecordingActivation : IPlaybackWindowsOutputNativeActivation
    {
        public bool OmitAudio { get; init; }
        public List<string> Released { get; } = [];

        public PlaybackWindowsOutputActivationResult InitializeComApartment(
            out IDisposable? apartment)
        {
            apartment = Resource("com");
            return PlaybackWindowsOutputActivationResult.Activated;
        }

        public PlaybackWindowsOutputActivationResult ActivateDirect3D11(
            out IDisposable? device)
        {
            device = Resource("video-resource");
            return PlaybackWindowsOutputActivationResult.Activated;
        }

        public PlaybackWindowsOutputActivationResult ActivateWasapiSharedEventDriven(
            out IDisposable? audio)
        {
            audio = OmitAudio ? null : Resource("audio-resource");
            return PlaybackWindowsOutputActivationResult.Activated;
        }

        private ResourceLease Resource(string name) => new(name, Released);
    }

    private sealed class ResourceLease(string name, List<string> released) : IDisposable
    {
        public void Dispose() => released.Add(name);
    }

    private sealed class RecordingBinding : IPlaybackWindowsOutputNativeResourceBinding
    {
        public PlaybackWindowsOutputNativeBindingResult VideoResult { get; init; } =
            PlaybackWindowsOutputNativeBindingResult.Bound;
        public PlaybackWindowsOutputNativeBindingResult AudioResult { get; init; } =
            PlaybackWindowsOutputNativeBindingResult.Bound;
        public bool ThrowOnAudio { get; init; }
        public bool BlockVideo { get; init; }
        public int VideoCalls { get; private set; }
        public List<string> Unbound { get; } = [];
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PlaybackWindowsOutputNativeBindingResult BindVideo(
            IDisposable borrowedResource,
            PlaybackVideoRendererConfiguration configuration)
        {
            VideoCalls++;
            Entered.TrySetResult();
            if (BlockVideo)
            {
                Release.Task.GetAwaiter().GetResult();
            }
            return VideoResult;
        }

        public PlaybackWindowsOutputNativeBindingResult BindAudio(
            IDisposable borrowedResource,
            PlaybackAudioDeviceConfiguration configuration)
        {
            if (ThrowOnAudio) throw new InvalidOperationException("fixed-test-exception");
            return AudioResult;
        }

        public void UnbindVideo() => Unbound.Add("video");
        public void UnbindAudio() => Unbound.Add("audio");
    }
}
