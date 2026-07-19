namespace PeerCast.Nova.Playback.Tests;

public sealed class BoundedDiagnosticPlaybackFixtureTests
{
    [Fact]
    public void RoundTrip_PreservesExactlyOneVideoAndOneAudioPacket()
    {
        var expected = CreateFixture();
        using var stream = new MemoryStream();

        Assert.Equal(
            BoundedDiagnosticPlaybackFixtureResult.Completed,
            BoundedDiagnosticPlaybackFixtureFormat.TryWrite(stream, expected));

        stream.Position = 0;
        Assert.Equal(
            BoundedDiagnosticPlaybackFixtureResult.Completed,
            BoundedDiagnosticPlaybackFixtureFormat.TryRead(stream, out var actual));

        Assert.NotNull(actual);
        Assert.Equal(expected.Video.Codec, actual.Video.Codec);
        Assert.Equal(expected.Video.PresentationTimestamp, actual.Video.PresentationTimestamp);
        Assert.Equal(expected.Video.Duration, actual.Video.Duration);
        Assert.Equal(expected.Video.Payload.ToArray(), actual.Video.Payload.ToArray());
        Assert.Equal(expected.Audio.Codec, actual.Audio.Codec);
        Assert.Equal(expected.Audio.PresentationTimestamp, actual.Audio.PresentationTimestamp);
        Assert.Equal(expected.Audio.Duration, actual.Audio.Duration);
        Assert.Equal(expected.Audio.Payload.ToArray(), actual.Audio.Payload.ToArray());
    }

    [Fact]
    public void TryRead_RejectsTrailingData()
    {
        using var stream = new MemoryStream();
        Assert.Equal(
            BoundedDiagnosticPlaybackFixtureResult.Completed,
            BoundedDiagnosticPlaybackFixtureFormat.TryWrite(stream, CreateFixture()));
        stream.WriteByte(0x7f);
        stream.Position = 0;

        Assert.Equal(
            BoundedDiagnosticPlaybackFixtureResult.TrailingData,
            BoundedDiagnosticPlaybackFixtureFormat.TryRead(stream, out var fixture));
        Assert.Null(fixture);
    }

    [Fact]
    public void TryRead_RejectsTruncatedPayload()
    {
        using var complete = new MemoryStream();
        Assert.Equal(
            BoundedDiagnosticPlaybackFixtureResult.Completed,
            BoundedDiagnosticPlaybackFixtureFormat.TryWrite(complete, CreateFixture()));

        var bytes = complete.ToArray();
        using var truncated = new MemoryStream(bytes[..^1]);

        Assert.Equal(
            BoundedDiagnosticPlaybackFixtureResult.Truncated,
            BoundedDiagnosticPlaybackFixtureFormat.TryRead(truncated, out var fixture));
        Assert.Null(fixture);
    }

    [Fact]
    public void TryWrite_RejectsUnsupportedCodecAndOversizedPayload()
    {
        var unsupported = CreateFixture() with
        {
            Video = new(
                BoundedDiagnosticPlaybackCodec.Aac,
                0,
                1,
                new byte[] { 1 }),
        };
        using var unsupportedStream = new MemoryStream();
        Assert.Equal(
            BoundedDiagnosticPlaybackFixtureResult.UnsupportedCodec,
            BoundedDiagnosticPlaybackFixtureFormat.TryWrite(unsupportedStream, unsupported));

        var oversized = CreateFixture() with
        {
            Audio = new(
                BoundedDiagnosticPlaybackCodec.Aac,
                0,
                1,
                new byte[BoundedDiagnosticPlaybackFixtureFormat.MaximumAudioPayloadLength + 1]),
        };
        using var oversizedStream = new MemoryStream();
        Assert.Equal(
            BoundedDiagnosticPlaybackFixtureResult.LimitExceeded,
            BoundedDiagnosticPlaybackFixtureFormat.TryWrite(oversizedStream, oversized));
    }

    [Fact]
    public void TryRead_RejectsInvalidHeaderWithoutAllocatingPayloads()
    {
        using var stream = new MemoryStream(new byte[BoundedDiagnosticPlaybackFixtureFormat.HeaderLength]);

        Assert.Equal(
            BoundedDiagnosticPlaybackFixtureResult.InvalidHeader,
            BoundedDiagnosticPlaybackFixtureFormat.TryRead(stream, out var fixture));
        Assert.Null(fixture);
    }

    private static BoundedDiagnosticPlaybackFixture CreateFixture() =>
        new(
            new(
                BoundedDiagnosticPlaybackCodec.H264,
                PresentationTimestamp: 0,
                Duration: 3_000,
                Payload: new byte[] { 0, 0, 0, 1, 0x65, 0x01 }),
            new(
                BoundedDiagnosticPlaybackCodec.Aac,
                PresentationTimestamp: 0,
                Duration: 1_024,
                Payload: new byte[] { 0x21, 0x10, 0x04 }));
}
