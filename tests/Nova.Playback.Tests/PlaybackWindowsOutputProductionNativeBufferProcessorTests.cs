using System.Runtime.Versioning;

namespace PeerCast.Nova.Playback.Tests;

[SupportedOSPlatform("windows")]
public sealed class PlaybackWindowsOutputProductionNativeBufferProcessorTests
{
    [Fact]
    public void ProductionSeamOrdersVideoUploadAndPresentAsOneCall()
    {
        var calls = new RecordingCalls();
        var processor = new SystemPlaybackWindowsOutputNativeBufferProcessor(calls);
        var result = processor.ProcessVideo(new VideoResource(),
            new(PlaybackDecodedVideoPixelFormat.Bgra32, 2, 2, 8, new byte[16], 1));
        Assert.Equal(PlaybackWindowsOutputNativeBufferResult.Processed, result);
        Assert.Equal(["upload-present"], calls.Order);
    }

    [Fact]
    public void ProductionSeamEnsuresRenderClientThenWritesAndStarts()
    {
        var order = new List<string>();
        var calls = new RecordingCalls(order);
        var processor = new SystemPlaybackWindowsOutputNativeBufferProcessor(calls);
        var result = processor.ProcessAudio(new AudioResource(order),
            new(PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                48_000, 2, 2, new byte[8], 1), true);
        Assert.Equal(PlaybackWindowsOutputNativeBufferResult.Processed, result);
        Assert.Equal(["client", "ensure", "render", "write-start"], order);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0x087A0001, 1)]
    [InlineData(unchecked((int)0x887A0001), 3)]
    [InlineData(unchecked((int)0x887A0005), 4)]
    [InlineData(unchecked((int)0x88890004), 4)]
    [InlineData(unchecked((int)0x88890008), 2)]
    [InlineData(unchecked((int)0x88890006), 1)]
    [InlineData(unchecked((int)0x81234567), 5)]
    public void HResultMappingIsFiniteAndUnknownFailsClosed(
        int hresult, int expected) =>
        Assert.Equal((PlaybackWindowsOutputNativeBufferResult)expected,
            SystemPlaybackWindowsOutputNativeBufferProcessor.Normalize(hresult));

    [Fact]
    public void ResetStopsAudioOncePerRequestAndDoesNotOwnResources()
    {
        var calls = new RecordingCalls();
        var processor = new SystemPlaybackWindowsOutputNativeBufferProcessor(calls);
        var audio = new AudioResource([]);
        processor.ResetAudio(audio);
        processor.ResetVideo(new VideoResource());
        Assert.Equal(["stop"], calls.Order);
        Assert.False(audio.Disposed);
    }

    private sealed class RecordingCalls : IPlaybackWindowsOutputNativeBufferCalls
    {
        public RecordingCalls() : this([]) { }
        public RecordingCalls(List<string> order) => Order = order;
        public List<string> Order { get; }
        public int UploadAndPresent(IPlaybackWindowsDirect3D11BorrowedResource resource,
            PlaybackWindowsVideoFrameBuffer frame)
        { Order.Add("upload-present"); return 0; }
        public int WriteAudio(IPlaybackWindowsWasapiBorrowedResource resource,
            PlaybackWindowsAudioSampleBuffer samples, bool startStream)
        { Order.Add(startStream ? "write-start" : "write"); return 0; }
        public void StopAudio(IPlaybackWindowsWasapiBorrowedResource resource) => Order.Add("stop");
    }
    private sealed class VideoResource : IDisposable, IPlaybackWindowsDirect3D11BorrowedResource
    {
        public nint Device => 1;
        public nint ImmediateContext => 1;
        public nint SwapChain => 1;
        public nint RenderTargetView => 1;
        public void ReplaceOutputResources(nint swapChain, nint view) { }
        public void ClearOutputResources() { }
        public void Dispose() { }
    }
    private sealed class AudioResource(List<string> order) : IDisposable, IPlaybackWindowsWasapiBorrowedResource
    {
        public bool Disposed { get; private set; }
        public nint AudioClient { get { order.Add("client"); return 1; } }
        public nint AudioRenderClient { get { order.Add("render"); return 1; } }
        public nint EventHandle => 1;
        public bool EnsureAudioRenderClient() { order.Add("ensure"); return true; }
        public bool RecreateAudioClient() => true;
        public void ResetAudioClient() { }
        public void Dispose() => Disposed = true;
    }
}
