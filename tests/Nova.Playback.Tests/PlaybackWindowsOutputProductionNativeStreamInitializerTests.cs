using System.Runtime.Versioning;

namespace PeerCast.Nova.Playback.Tests;

[SupportedOSPlatform("windows")]
public sealed class PlaybackWindowsOutputProductionNativeStreamInitializerTests
{
    [Fact]
    public void VideoResolvesTypedResourceThenOpaqueWindowIdentity()
    {
        var calls = new List<string>();
        var resource = new RecordingD3D11Resource(calls);
        var initializer = new SystemPlaybackWindowsOutputNativeStreamInitializer(
            new RejectingWindowResolver(calls));

        var result = initializer.InitializeVideo(resource,
            new(41, 640, 360, PlaybackDecodedVideoPixelFormat.Bgra32, 1));

        Assert.Equal(PlaybackWindowsOutputNativeStreamResult.Failure, result);
        Assert.Equal(["device", "window"], calls);
        Assert.Equal(0, resource.ReplaceCount);
    }

    [Fact]
    public void AudioChecksOwnedEventThenRecreatesClientBeforeComAccess()
    {
        var calls = new List<string>();
        var resource = new RecordingWasapiResource(calls);
        var initializer = new SystemPlaybackWindowsOutputNativeStreamInitializer(
            new RejectingWindowResolver([]));

        var result = initializer.InitializeAudio(resource,
            new(PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                48_000, 2, TimeSpan.FromMilliseconds(100), 1));

        Assert.Equal(PlaybackWindowsOutputNativeStreamResult.Failure, result);
        Assert.Equal(["event", "recreate"], calls);
    }

    [Theory]
    [InlineData(unchecked((int)0x887A0005), 3)]
    [InlineData(unchecked((int)0x887A0006), 3)]
    [InlineData(unchecked((int)0x887A0007), 3)]
    [InlineData(unchecked((int)0x88890004), 3)]
    [InlineData(unchecked((int)0x88890008), 1)]
    [InlineData(unchecked((int)0x887A0001), 2)]
    [InlineData(unchecked((int)0x81234567), 4)]
    public void HResultMappingIsFixedAndUnknownFailsClosed(
        int hresult, int expected)
    {
        Assert.Equal((PlaybackWindowsOutputNativeStreamResult)expected,
            SystemPlaybackWindowsOutputNativeStreamInitializer.MapHResult(hresult));
    }

    [Fact]
    public void ResetDelegatesCleanupToActivationOwnedResources()
    {
        var initializer = new SystemPlaybackWindowsOutputNativeStreamInitializer(
            new RejectingWindowResolver([]));
        var video = new RecordingD3D11Resource([]);
        var audio = new RecordingWasapiResource([]);

        initializer.ResetAudio(audio);
        initializer.ResetVideo(video);

        Assert.Equal(1, audio.ResetCount);
        Assert.Equal(1, video.ClearCount);
    }

    private sealed class RejectingWindowResolver(List<string> calls) :
        IPlaybackWindowsWindowIdentityResolver
    {
        public bool TryResolve(long token, out nint window)
        {
            calls.Add("window");
            window = 0;
            return false;
        }
    }

    private sealed class RecordingD3D11Resource(List<string> calls) :
        IDisposable, IPlaybackWindowsDirect3D11BorrowedResource
    {
        public nint Device { get { calls.Add("device"); return 1; } }
        public int ReplaceCount { get; private set; }
        public int ClearCount { get; private set; }
        public void ReplaceOutputResources(nint swapChain, nint renderTargetView) =>
            ReplaceCount++;
        public void ClearOutputResources() => ClearCount++;
        public void Dispose() { }
    }

    private sealed class RecordingWasapiResource(List<string> calls) :
        IDisposable, IPlaybackWindowsWasapiBorrowedResource
    {
        public nint AudioClient { get { calls.Add("client"); return 0; } }
        public nint EventHandle { get { calls.Add("event"); return 1; } }
        public int ResetCount { get; private set; }
        public bool RecreateAudioClient() { calls.Add("recreate"); return false; }
        public void ResetAudioClient() => ResetCount++;
        public void Dispose() { }
    }
}
