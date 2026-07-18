namespace PeerCast.Nova.Playback.Tests;

#pragma warning disable CA1861 // Inline expected sequences keep cleanup assertions local.

public sealed class PlaybackWindowsOutputActivationBoundaryTests
{
    [Fact]
    public void SuccessOwnsAllResourcesUntilReverseOrderDispose()
    {
        var native = new RecordingNativeActivation();
        var boundary = Create(native);

        Assert.Equal(PlaybackWindowsOutputActivationResult.Activated, boundary.Activate());
        Assert.Empty(native.Released);

        boundary.Dispose();

        Assert.Equal(new[] { "audio", "video", "com" }, native.Released);
    }

    [Theory]
    [InlineData(PlaybackWindowsOutputActivationResult.ApiUnavailable)]
    [InlineData(PlaybackWindowsOutputActivationResult.DeviceUnavailable)]
    [InlineData(PlaybackWindowsOutputActivationResult.PermissionDenied)]
    [InlineData(PlaybackWindowsOutputActivationResult.InvalidEnvironment)]
    [InlineData(PlaybackWindowsOutputActivationResult.Failure)]
    public void AudioFailureNormalizesAndRollsBackPartialSuccess(
        PlaybackWindowsOutputActivationResult expected)
    {
        var native = new RecordingNativeActivation { AudioResult = expected };
        using var boundary = Create(native);

        Assert.Equal(expected, boundary.Activate());
        Assert.Equal(new[] { "audio-partial", "video", "com" }, native.Released);
    }

    [Fact]
    public void VideoFailureRollsBackComAndItsPartialResource()
    {
        var native = new RecordingNativeActivation
        {
            VideoResult = PlaybackWindowsOutputActivationResult.DeviceUnavailable,
        };
        using var boundary = Create(native);

        Assert.Equal(PlaybackWindowsOutputActivationResult.DeviceUnavailable,
            boundary.Activate());
        Assert.Equal(new[] { "video-partial", "com" }, native.Released);
        Assert.Equal(0, native.AudioCalls);
    }

    [Fact]
    public void UndefinedResultAndExceptionFailClosedAndCleanUp()
    {
        var undefined = new RecordingNativeActivation
        {
            VideoResult = (PlaybackWindowsOutputActivationResult)99,
        };
        var throwing = new RecordingNativeActivation { ThrowOnAudio = true };
        using var undefinedBoundary = Create(undefined);
        using var throwingBoundary = Create(throwing);

        Assert.Equal(PlaybackWindowsOutputActivationResult.Failure,
            undefinedBoundary.Activate());
        Assert.Equal(new[] { "video-partial", "com" }, undefined.Released);
        Assert.Equal(PlaybackWindowsOutputActivationResult.Failure,
            throwingBoundary.Activate());
        Assert.Equal(new[] { "video", "com" }, throwing.Released);
    }

    [Fact]
    public void NonWindowsDoesNotAccessNativeBoundary()
    {
        var native = new RecordingNativeActivation();
        using var boundary = new PlaybackWindowsOutputActivationBoundary(native,
            static () => false);

        Assert.Equal(PlaybackWindowsOutputActivationResult.PlatformUnsupported,
            boundary.Activate());
        Assert.Equal(0, native.ComCalls);
    }

    [Fact]
    public void DisposeIsIdempotentAndClosesAdmission()
    {
        var native = new RecordingNativeActivation();
        var boundary = Create(native);
        Assert.Equal(PlaybackWindowsOutputActivationResult.Activated, boundary.Activate());

        boundary.Dispose();
        boundary.Dispose();

        Assert.Equal(PlaybackWindowsOutputActivationResult.Disposed, boundary.Activate());
        Assert.Equal(new[] { "audio", "video", "com" }, native.Released);
    }

    [Fact]
    public void MissingResourceOnReportedSuccessFailsClosed()
    {
        var native = new RecordingNativeActivation { OmitVideoResource = true };
        using var boundary = Create(native);

        Assert.Equal(PlaybackWindowsOutputActivationResult.Failure, boundary.Activate());
        Assert.Equal(new[] { "com" }, native.Released);
    }

    private static PlaybackWindowsOutputActivationBoundary Create(
        IPlaybackWindowsOutputNativeActivation native) =>
        new(native, static () => true);

    private sealed class RecordingNativeActivation :
        IPlaybackWindowsOutputNativeActivation
    {
        public PlaybackWindowsOutputActivationResult VideoResult { get; init; } =
            PlaybackWindowsOutputActivationResult.Activated;
        public PlaybackWindowsOutputActivationResult AudioResult { get; init; } =
            PlaybackWindowsOutputActivationResult.Activated;
        public bool ThrowOnAudio { get; init; }
        public bool OmitVideoResource { get; init; }
        public int ComCalls { get; private set; }
        public int AudioCalls { get; private set; }
        public List<string> Released { get; } = [];

        public PlaybackWindowsOutputActivationResult InitializeComApartment(
            out IDisposable? apartment)
        {
            ComCalls++;
            apartment = Resource("com");
            return PlaybackWindowsOutputActivationResult.Activated;
        }

        public PlaybackWindowsOutputActivationResult ActivateDirect3D11(
            out IDisposable? device)
        {
            device = OmitVideoResource ? null : Resource(
                VideoResult == PlaybackWindowsOutputActivationResult.Activated
                    ? "video" : "video-partial");
            return VideoResult;
        }

        public PlaybackWindowsOutputActivationResult ActivateWasapiSharedEventDriven(
            out IDisposable? audio)
        {
            AudioCalls++;
            if (ThrowOnAudio)
            {
                throw new InvalidOperationException("fixed-test-exception");
            }

            audio = Resource(AudioResult == PlaybackWindowsOutputActivationResult.Activated
                ? "audio" : "audio-partial");
            return AudioResult;
        }

        private RecordingResource Resource(string name) =>
            new(name, Released);
    }

    private sealed class RecordingResource(string name, List<string> released) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            released.Add(name);
        }
    }
}
