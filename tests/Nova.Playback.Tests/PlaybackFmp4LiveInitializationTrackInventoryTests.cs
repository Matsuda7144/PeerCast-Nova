using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveInitializationTrackInventoryTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Mvhd = 0x6d766864;
    private const uint Trak = 0x7472616b;

    [Fact]
    public void Project_InitializationTracksAreInventoried()
    {
        var segment = new byte[40];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 32, Moov);
        WriteHeader(segment.AsSpan(16, 8), 8, Mvhd);
        WriteHeader(segment.AsSpan(24, 8), 8, Trak);
        WriteHeader(segment.AsSpan(32, 8), 8, Trak);

        var result = PlaybackFmp4LiveInitializationTrackInventoryProjector.Project(
            segment,
            segment.Length,
            out var inventory);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackInventoryProjectionResult.Projected,
            result);
        var projected = Assert.IsType<PlaybackFmp4LiveInitializationTrackInventory>(
            inventory);
        Assert.Equal(PlaybackFmp4LiveSegmentKind.Initialization, projected.Envelope.Kind);
        Assert.Equal(2, projected.Tracks.Count);
        Assert.Equal(24, projected.Tracks[0].Offset);
        Assert.Equal(8, projected.Tracks[0].BoxLength);
        Assert.Equal(Trak, projected.Tracks[0].Header.Type);
        Assert.Equal(32, projected.Tracks[1].Offset);
        Assert.Equal(8, projected.Tracks[1].BoxLength);
        Assert.Equal(Trak, projected.Tracks[1].Header.Type);
    }

    [Fact]
    public void Project_TrackLimitIsEnforcedWithoutInventory()
    {
        var segment = new byte[40];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), 32, Moov);
        WriteHeader(segment.AsSpan(16, 8), 8, Mvhd);
        WriteHeader(segment.AsSpan(24, 8), 8, Trak);
        WriteHeader(segment.AsSpan(32, 8), 8, Trak);

        var result = PlaybackFmp4LiveInitializationTrackInventoryProjector.Project(
            segment,
            segment.Length,
            out var inventory,
            new() { MaximumTrackCount = 1 });

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackInventoryProjectionResult
                .TrackLimitExceeded,
            result);
        Assert.Null(inventory);
    }

    private static void WriteHeader(Span<byte> destination, uint size, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], size);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], type);
    }
}
