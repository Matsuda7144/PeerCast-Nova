using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveInitializationTrackHandlerProjectionTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Trak = 0x7472616b;
    private const uint Mdia = 0x6d646961;
    private const uint Hdlr = 0x68646c72;
    private const uint Vide = 0x76696465;
    private const uint Soun = 0x736f756e;

    [Fact]
    public void Project_VideoAndAudioTrackHandlersAreProjected()
    {
        var segment = new byte[114];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 106, Moov);
        WriteTrack(segment.AsSpan(16, 49), Vide);
        WriteTrack(segment.AsSpan(65, 49), Soun);

        var result = PlaybackFmp4LiveInitializationTrackHandlerProjector.Project(
            segment,
            segment.Length,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackHandlerProjectionResult.Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveInitializationTrackHandlerProjection>(projection);
        Assert.Equal(2, projected.Tracks.Count);
        Assert.Same(projected.Source.Tracks[0], projected.Tracks[0].Track);
        Assert.Equal(PlaybackIsoBmffHandlerKind.Video, projected.Tracks[0].Kind);
        Assert.Equal(Vide, projected.Tracks[0].Handler.HandlerType);
        Assert.Same(projected.Source.Tracks[1], projected.Tracks[1].Track);
        Assert.Equal(PlaybackIsoBmffHandlerKind.Audio, projected.Tracks[1].Kind);
        Assert.Equal(Soun, projected.Tracks[1].Handler.HandlerType);
    }

    private static void WriteTrack(Span<byte> destination, uint handlerType)
    {
        WriteHeader(destination[..8], 49, Trak);
        WriteHeader(destination.Slice(8, 8), 41, Mdia);
        WriteHandler(destination.Slice(16, 33), handlerType);
    }

    private static void WriteHandler(Span<byte> destination, uint handlerType)
    {
        WriteHeader(destination[..8], 33, Hdlr);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(16, 4), handlerType);
    }

    private static void WriteHeader(Span<byte> destination, uint size, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], size);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], type);
    }
}
