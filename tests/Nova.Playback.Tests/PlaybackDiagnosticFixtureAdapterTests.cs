namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDiagnosticFixtureAdapterTests
{
    [Fact]
    public void TryLoad_MapsExactlyOneVideoAndOneAudioPacket()
    {
        var fixture = new BoundedDiagnosticPlaybackFixture(
            new(BoundedDiagnosticPlaybackCodec.H264, 10, 2, new byte[] { 1, 2, 3 }),
            new(BoundedDiagnosticPlaybackCodec.Aac, 12, 3, new byte[] { 4, 5 }));
        using var stream = new MemoryStream();
        Assert.Equal(
            BoundedDiagnosticPlaybackFixtureResult.Completed,
            BoundedDiagnosticPlaybackFixtureFormat.TryWrite(stream, fixture));
        var bytes = stream.ToArray();

        var result = PlaybackDiagnosticFixtureAdapter.TryLoad(
            () => new MemoryStream(bytes, writable: false),
            CancellationToken.None,
            out var input);

        Assert.Equal(PlaybackDiagnosticFixtureAdapterResult.Completed, result);
        Assert.NotNull(input);
        Assert.Equal(BoundedDiagnosticPlaybackCodec.H264, input.Video.Codec);
        Assert.Equal(10, input.Video.PresentationTimestamp);
        Assert.Equal(2, input.Video.Duration);
        Assert.Equal(new byte[] { 1, 2, 3 }, input.Video.Payload.ToArray());
        Assert.Equal(BoundedDiagnosticPlaybackCodec.Aac, input.Audio.Codec);
        Assert.Equal(12, input.Audio.PresentationTimestamp);
        Assert.Equal(3, input.Audio.Duration);
        Assert.Equal(new byte[] { 4, 5 }, input.Audio.Payload.ToArray());
    }

    [Fact]
    public void TryLoad_RejectedFixtureReturnsFixedResult()
    {
        var result = PlaybackDiagnosticFixtureAdapter.TryLoad(
            () => new MemoryStream(new byte[BoundedDiagnosticPlaybackFixtureFormat.HeaderLength], writable: false),
            CancellationToken.None,
            out var input);

        Assert.Equal(PlaybackDiagnosticFixtureAdapterResult.FixtureRejected, result);
        Assert.Null(input);
    }

    [Fact]
    public void TryLoad_CancelledBeforeFactoryDoesNotOpenStream()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factoryCalled = false;

        var result = PlaybackDiagnosticFixtureAdapter.TryLoad(
            () =>
            {
                factoryCalled = true;
                return new MemoryStream();
            },
            cancellation.Token,
            out var input);

        Assert.Equal(PlaybackDiagnosticFixtureAdapterResult.Cancelled, result);
        Assert.False(factoryCalled);
        Assert.Null(input);
    }

    [Fact]
    public void TryLoad_DisposesFactoryOwnedStreamAfterRead()
    {
        var fixture = new BoundedDiagnosticPlaybackFixture(
            new(BoundedDiagnosticPlaybackCodec.H264, 0, 1, new byte[] { 1 }),
            new(BoundedDiagnosticPlaybackCodec.Aac, 0, 1, new byte[] { 2 }));
        using var source = new MemoryStream();
        Assert.Equal(
            BoundedDiagnosticPlaybackFixtureResult.Completed,
            BoundedDiagnosticPlaybackFixtureFormat.TryWrite(source, fixture));
        var stream = new TrackingMemoryStream(source.ToArray());

        var result = PlaybackDiagnosticFixtureAdapter.TryLoad(
            () => stream,
            CancellationToken.None,
            out _);

        Assert.Equal(PlaybackDiagnosticFixtureAdapterResult.Completed, result);
        Assert.True(stream.WasDisposed);
    }

    private sealed class TrackingMemoryStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
