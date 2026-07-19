using System.Buffers.Binary;

namespace PeerCast.Nova.Playback;

public enum BoundedDiagnosticPlaybackFixtureResult
{
    Completed,
    InvalidHeader,
    UnsupportedVersion,
    UnsupportedCodec,
    InvalidTiming,
    LimitExceeded,
    Truncated,
    TrailingData,
    Faulted,
}

public enum BoundedDiagnosticPlaybackCodec : byte
{
    H264 = 1,
    Aac = 2,
}

public readonly record struct BoundedDiagnosticPlaybackPacket(
    BoundedDiagnosticPlaybackCodec Codec,
    long PresentationTimestamp,
    long Duration,
    ReadOnlyMemory<byte> Payload);

public sealed record BoundedDiagnosticPlaybackFixture(
    BoundedDiagnosticPlaybackPacket Video,
    BoundedDiagnosticPlaybackPacket Audio);

/// <summary>
/// Diagnostic-only, finite fixture format. This is not a general media demuxer
/// and is not referenced by normal playback activation.
/// </summary>
public static class BoundedDiagnosticPlaybackFixtureFormat
{
    private static ReadOnlySpan<byte> Magic => "PCN8EFX1"u8;

    public const byte Version = 1;
    public const int MaximumVideoPayloadLength = 1024 * 1024;
    public const int MaximumAudioPayloadLength = 256 * 1024;
    public const int HeaderLength = 52;

    public static BoundedDiagnosticPlaybackFixtureResult TryWrite(
        Stream destination,
        BoundedDiagnosticPlaybackFixture? fixture)
    {
        if (destination is null || fixture is null || !destination.CanWrite)
            return BoundedDiagnosticPlaybackFixtureResult.Faulted;

        var validation = ValidateFixture(fixture);
        if (validation != BoundedDiagnosticPlaybackFixtureResult.Completed)
            return validation;

        try
        {
            Span<byte> header = stackalloc byte[HeaderLength];
            Magic.CopyTo(header);
            header[8] = Version;
            header[9] = (byte)fixture.Video.Codec;
            header[10] = (byte)fixture.Audio.Codec;
            header[11] = 0;
            BinaryPrimitives.WriteInt64LittleEndian(header[12..20], fixture.Video.PresentationTimestamp);
            BinaryPrimitives.WriteInt64LittleEndian(header[20..28], fixture.Video.Duration);
            BinaryPrimitives.WriteInt64LittleEndian(header[28..36], fixture.Audio.PresentationTimestamp);
            BinaryPrimitives.WriteInt64LittleEndian(header[36..44], fixture.Audio.Duration);
            BinaryPrimitives.WriteInt32LittleEndian(header[44..48], fixture.Video.Payload.Length);
            BinaryPrimitives.WriteInt32LittleEndian(header[48..52], fixture.Audio.Payload.Length);

            destination.Write(header);
            destination.Write(fixture.Video.Payload.Span);
            destination.Write(fixture.Audio.Payload.Span);
            return BoundedDiagnosticPlaybackFixtureResult.Completed;
        }
        catch
        {
            return BoundedDiagnosticPlaybackFixtureResult.Faulted;
        }
    }

    public static BoundedDiagnosticPlaybackFixtureResult TryRead(
        Stream source,
        out BoundedDiagnosticPlaybackFixture? fixture)
    {
        fixture = null;
        if (source is null || !source.CanRead)
            return BoundedDiagnosticPlaybackFixtureResult.Faulted;

        try
        {
            Span<byte> header = stackalloc byte[HeaderLength];
            source.ReadExactly(header);

            if (!header[..8].SequenceEqual(Magic))
                return BoundedDiagnosticPlaybackFixtureResult.InvalidHeader;
            if (header[8] != Version)
                return BoundedDiagnosticPlaybackFixtureResult.UnsupportedVersion;
            if (header[9] != (byte)BoundedDiagnosticPlaybackCodec.H264 ||
                header[10] != (byte)BoundedDiagnosticPlaybackCodec.Aac ||
                header[11] != 0)
            {
                return BoundedDiagnosticPlaybackFixtureResult.UnsupportedCodec;
            }

            var videoTimestamp = BinaryPrimitives.ReadInt64LittleEndian(header[12..20]);
            var videoDuration = BinaryPrimitives.ReadInt64LittleEndian(header[20..28]);
            var audioTimestamp = BinaryPrimitives.ReadInt64LittleEndian(header[28..36]);
            var audioDuration = BinaryPrimitives.ReadInt64LittleEndian(header[36..44]);
            var videoLength = BinaryPrimitives.ReadInt32LittleEndian(header[44..48]);
            var audioLength = BinaryPrimitives.ReadInt32LittleEndian(header[48..52]);

            if (videoTimestamp < 0 || audioTimestamp < 0 || videoDuration <= 0 || audioDuration <= 0)
                return BoundedDiagnosticPlaybackFixtureResult.InvalidTiming;
            if (videoLength <= 0 || videoLength > MaximumVideoPayloadLength ||
                audioLength <= 0 || audioLength > MaximumAudioPayloadLength)
            {
                return BoundedDiagnosticPlaybackFixtureResult.LimitExceeded;
            }

            var videoPayload = new byte[videoLength];
            var audioPayload = new byte[audioLength];
            source.ReadExactly(videoPayload);
            source.ReadExactly(audioPayload);

            if (source.ReadByte() != -1)
                return BoundedDiagnosticPlaybackFixtureResult.TrailingData;

            fixture = new(
                new(BoundedDiagnosticPlaybackCodec.H264, videoTimestamp, videoDuration, videoPayload),
                new(BoundedDiagnosticPlaybackCodec.Aac, audioTimestamp, audioDuration, audioPayload));
            return BoundedDiagnosticPlaybackFixtureResult.Completed;
        }
        catch (EndOfStreamException)
        {
            return BoundedDiagnosticPlaybackFixtureResult.Truncated;
        }
        catch
        {
            return BoundedDiagnosticPlaybackFixtureResult.Faulted;
        }
    }

    private static BoundedDiagnosticPlaybackFixtureResult ValidateFixture(
        BoundedDiagnosticPlaybackFixture fixture)
    {
        if (fixture.Video.Codec != BoundedDiagnosticPlaybackCodec.H264 ||
            fixture.Audio.Codec != BoundedDiagnosticPlaybackCodec.Aac)
        {
            return BoundedDiagnosticPlaybackFixtureResult.UnsupportedCodec;
        }

        if (fixture.Video.PresentationTimestamp < 0 || fixture.Audio.PresentationTimestamp < 0 ||
            fixture.Video.Duration <= 0 || fixture.Audio.Duration <= 0)
        {
            return BoundedDiagnosticPlaybackFixtureResult.InvalidTiming;
        }

        if (fixture.Video.Payload.Length <= 0 ||
            fixture.Video.Payload.Length > MaximumVideoPayloadLength ||
            fixture.Audio.Payload.Length <= 0 ||
            fixture.Audio.Payload.Length > MaximumAudioPayloadLength)
        {
            return BoundedDiagnosticPlaybackFixtureResult.LimitExceeded;
        }

        return BoundedDiagnosticPlaybackFixtureResult.Completed;
    }
}
