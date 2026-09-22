using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionCompatibilityTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Trak = 0x7472616b;
    private const uint Mdia = 0x6d646961;
    private const uint Hdlr = 0x68646c72;
    private const uint Minf = 0x6d696e66;
    private const uint Stbl = 0x7374626c;
    private const uint Stsd = 0x73747364;
    private const uint Vide = 0x76696465;
    private const uint Avc1 = 0x61766331;
    private const uint UnknownSibling = 0x78787878;

    [Fact]
    public void Project_UnknownSampleTableSiblingDoesNotHideRequiredSampleDescription()
    {
        var segment = new byte[113];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 105, Moov);
        WriteHeader(segment.AsSpan(16, 8), 97, Trak);
        WriteHeader(segment.AsSpan(24, 8), 89, Mdia);
        WriteHandler(segment.AsSpan(32, 33), Vide);
        WriteHeader(segment.AsSpan(65, 8), 48, Minf);
        WriteHeader(segment.AsSpan(73, 8), 40, Stbl);
        WriteHeader(segment.AsSpan(81, 8), 8, UnknownSibling);
        WriteSampleDescription(segment.AsSpan(89, 24), Avc1);

        var result =
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjection>(
                projection);
        var track = Assert.Single(projected.Source.Tracks);
        Assert.Equal(PlaybackIsoBmffHandlerKind.Video, track.Kind);
        Assert.Equal(Vide, track.Handler.HandlerType);
    }

    private static void WriteHandler(Span<byte> destination, uint handlerType)
    {
        WriteHeader(destination[..8], 33, Hdlr);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(16, 4), handlerType);
    }

    private static void WriteSampleDescription(
        Span<byte> destination,
        uint sampleEntryType)
    {
        WriteHeader(destination[..8], 24, Stsd);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(12, 4), 1);
        WriteHeader(destination.Slice(16, 8), 8, sampleEntryType);
    }

    private static void WriteHeader(Span<byte> destination, uint size, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], size);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], type);
    }
}
