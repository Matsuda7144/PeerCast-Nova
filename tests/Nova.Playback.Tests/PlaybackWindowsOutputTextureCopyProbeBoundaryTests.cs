namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackWindowsOutputTextureCopyProbeBoundaryTests
{
    [Fact]
    public async Task PresentsOneExactBgraFrameThroughInjectedNativePath()
    {
        using var setup = await SetupAsync();
        var native = new RecordingNative();
        using var boundary = new PlaybackWindowsOutputTextureCopyProbeBoundary(
            setup.Activation,
            setup.Binding,
            setup.Initialization,
            native,
            static () => true);

        var frame = Video();
        Assert.Equal(
            PlaybackWindowsOutputTextureCopyProbeResult.Presented,
            await boundary.PresentAsync(frame));
        Assert.Equal(1, native.CallCount);
        Assert.Equal(frame, native.Frame);
    }

    [Fact]
    public async Task InvalidLayoutAndGenerationAvoidNativeAccess()
    {
        using var setup = await SetupAsync();
        var native = new RecordingNative();
        using var boundary = new PlaybackWindowsOutputTextureCopyProbeBoundary(
            setup.Activation,
            setup.Binding,
            setup.Initialization,
            native,
            static () => true);

        Assert.Equal(
            PlaybackWindowsOutputTextureCopyProbeResult.InvalidConfiguration,
            await boundary.PresentAsync(Video() with { Stride = 7 }));
        Assert.Equal(
            PlaybackWindowsOutputTextureCopyProbeResult.InitializationRequired,
            await boundary.PresentAsync(Video() with { Generation = 8 }));
        Assert.Equal(0, native.CallCount);
    }

    [Theory]
    [InlineData(
        (int)PlaybackWindowsOutputTextureCopyProbeNativeResult.WouldBlock,
        PlaybackWindowsOutputTextureCopyProbeResult.WouldBlock)]
    [InlineData(
        (int)PlaybackWindowsOutputTextureCopyProbeNativeResult.ResizeRequired,
        PlaybackWindowsOutputTextureCopyProbeResult.ResizeRequired)]
    [InlineData(
        (int)PlaybackWindowsOutputTextureCopyProbeNativeResult.DeviceLost,
        PlaybackWindowsOutputTextureCopyProbeResult.DeviceLost)]
    [InlineData(
        (int)PlaybackWindowsOutputTextureCopyProbeNativeResult.FormatUnsupported,
        PlaybackWindowsOutputTextureCopyProbeResult.FormatUnsupported)]
    [InlineData(
        (int)PlaybackWindowsOutputTextureCopyProbeNativeResult.Failure,
        PlaybackWindowsOutputTextureCopyProbeResult.Failure)]
    public async Task NativeResultsRemainFixed(
        int nativeResult,
        PlaybackWindowsOutputTextureCopyProbeResult expected)
    {
        using var setup = await SetupAsync();
        using var boundary = new PlaybackWindowsOutputTextureCopyProbeBoundary(
            setup.Activation,
            setup.Binding,
            setup.Initialization,
            new RecordingNative
            {
                Result = (PlaybackWindowsOutputTextureCopyProbeNativeResult)nativeResult,
            },
            static () => true);

        Assert.Equal(expected, await boundary.PresentAsync(Video()));
    }

    [Fact]
    public void ExactTextureCopyPlanPreservesDimensionsPitchAndBytes()
    {
        Assert.True(PlaybackWindowsD3D11TextureCopyPlan.TryCreate(
            new(
                PlaybackDecodedVideoPixelFormat.Bgra32,
                320,
                240,
                1280,
                new byte[307_200],
                1),
            out var plan));

        Assert.Equal(320u, plan.Width);
        Assert.Equal(240u, plan.Height);
        Assert.Equal(87u, plan.Format);
        Assert.Equal(1280u, plan.RowPitch);
        Assert.Equal(307_200u, plan.SlicePitch);
    }

    [Fact]
    public void TextureCopyPlanRejectsContradictoryPayload()
    {
        Assert.False(PlaybackWindowsD3D11TextureCopyPlan.TryCreate(
            new(
                PlaybackDecodedVideoPixelFormat.Bgra32,
                320,
                240,
                1280,
                new byte[16],
                1),
            out _));
    }

    [Theory]
    [InlineData(0, (int)PlaybackWindowsOutputTextureCopyProbeNativeResult.Presented)]
    [InlineData(0x087A0001,
        (int)PlaybackWindowsOutputTextureCopyProbeNativeResult.WouldBlock)]
    [InlineData(unchecked((int)0x887A0001),
        (int)PlaybackWindowsOutputTextureCopyProbeNativeResult.ResizeRequired)]
    [InlineData(unchecked((int)0x887A0005),
        (int)PlaybackWindowsOutputTextureCopyProbeNativeResult.DeviceLost)]
    [InlineData(unchecked((int)0x887A0004),
        (int)PlaybackWindowsOutputTextureCopyProbeNativeResult.FormatUnsupported)]
    [InlineData(unchecked((int)0x80004005),
        (int)PlaybackWindowsOutputTextureCopyProbeNativeResult.Failure)]
    public void HResultNormalizationIsFixed(int hresult, int expected)
    {
        Assert.Equal(
            (PlaybackWindowsOutputTextureCopyProbeNativeResult)expected,
            SystemPlaybackWindowsOutputTextureCopyProbeNative.Normalize(hresult));
    }

    private static PlaybackWindowsVideoFrameBuffer Video() =>
        new(
            PlaybackDecodedVideoPixelFormat.Bgra32,
            2,
            2,
            8,
            new byte[16],
            7);

    private static async Task<Setup> SetupAsync()
    {
        var activation = new PlaybackWindowsOutputActivationBoundary(
            new RecordingActivation(),
            static () => true);
        Assert.Equal(
            PlaybackWindowsOutputActivationResult.Activated,
            activation.Activate());
        var binding = new PlaybackWindowsOutputResourceBindingBoundary(
            activation,
            new RecordingBinding(),
            static () => true);
        Assert.Equal(
            PlaybackWindowsOutputBindingResult.Bound,
            await binding.ConfigureAsync(
                new(PlaybackDecodedVideoPixelFormat.Bgra32, 2, 2, 8, 7),
                new(
                    PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                    48_000,
                    2,
                    7),
                default));
        var initialization = new PlaybackWindowsOutputStreamInitializationBoundary(
            activation,
            binding,
            new RecordingInitializer(),
            static () => true);
        Assert.Equal(
            PlaybackWindowsOutputStreamInitializationResult.Initialized,
            await initialization.InitializeAsync(
                new(1, 2, 2, PlaybackDecodedVideoPixelFormat.Bgra32, 7),
                new(
                    PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                    48_000,
                    2,
                    TimeSpan.FromMilliseconds(100),
                    7),
                default));
        return new(activation, binding, initialization);
    }

    private sealed class Setup(
        PlaybackWindowsOutputActivationBoundary activation,
        PlaybackWindowsOutputResourceBindingBoundary binding,
        PlaybackWindowsOutputStreamInitializationBoundary initialization) : IDisposable
    {
        public PlaybackWindowsOutputActivationBoundary Activation { get; } = activation;
        public PlaybackWindowsOutputResourceBindingBoundary Binding { get; } = binding;
        public PlaybackWindowsOutputStreamInitializationBoundary Initialization { get; } = initialization;

        public void Dispose()
        {
            Initialization.Dispose();
            Binding.Dispose();
            Activation.Dispose();
        }
    }

    private sealed class RecordingNative : IPlaybackWindowsOutputTextureCopyProbeNative
    {
        public PlaybackWindowsOutputTextureCopyProbeNativeResult Result { get; init; } =
            PlaybackWindowsOutputTextureCopyProbeNativeResult.Presented;
        public int CallCount { get; private set; }
        public PlaybackWindowsVideoFrameBuffer Frame { get; private set; }

        public PlaybackWindowsOutputTextureCopyProbeNativeResult Present(
            IDisposable borrowedResource,
            PlaybackWindowsVideoFrameBuffer frame)
        {
            CallCount++;
            Frame = frame;
            return Result;
        }
    }

    private sealed class RecordingActivation : IPlaybackWindowsOutputNativeActivation
    {
        public PlaybackWindowsOutputActivationResult InitializeComApartment(
            out IDisposable? apartment)
        {
            apartment = new Lease();
            return PlaybackWindowsOutputActivationResult.Activated;
        }

        public PlaybackWindowsOutputActivationResult ActivateDirect3D11(
            out IDisposable? device)
        {
            device = new Lease();
            return PlaybackWindowsOutputActivationResult.Activated;
        }

        public PlaybackWindowsOutputActivationResult ActivateWasapiSharedEventDriven(
            out IDisposable? audio)
        {
            audio = new Lease();
            return PlaybackWindowsOutputActivationResult.Activated;
        }
    }

    private sealed class RecordingBinding : IPlaybackWindowsOutputNativeResourceBinding
    {
        public PlaybackWindowsOutputNativeBindingResult BindVideo(
            IDisposable resource,
            PlaybackVideoRendererConfiguration configuration) =>
            PlaybackWindowsOutputNativeBindingResult.Bound;

        public PlaybackWindowsOutputNativeBindingResult BindAudio(
            IDisposable resource,
            PlaybackAudioDeviceConfiguration configuration) =>
            PlaybackWindowsOutputNativeBindingResult.Bound;

        public void UnbindVideo() { }
        public void UnbindAudio() { }
    }

    private sealed class RecordingInitializer : IPlaybackWindowsOutputNativeStreamInitializer
    {
        public PlaybackWindowsOutputNativeStreamResult InitializeVideo(
            IDisposable resource,
            PlaybackWindowsVideoStreamConfiguration configuration) =>
            PlaybackWindowsOutputNativeStreamResult.Initialized;

        public PlaybackWindowsOutputNativeStreamResult ResizeVideo(
            IDisposable resource,
            PlaybackWindowsVideoStreamConfiguration configuration) =>
            PlaybackWindowsOutputNativeStreamResult.Initialized;

        public PlaybackWindowsOutputNativeStreamResult InitializeAudio(
            IDisposable resource,
            PlaybackWindowsAudioStreamConfiguration configuration) =>
            PlaybackWindowsOutputNativeStreamResult.Initialized;

        public void ResetVideo(IDisposable resource) { }
        public void ResetAudio(IDisposable resource) { }
    }

    private sealed class Lease : IDisposable
    {
        public void Dispose() { }
    }
}
