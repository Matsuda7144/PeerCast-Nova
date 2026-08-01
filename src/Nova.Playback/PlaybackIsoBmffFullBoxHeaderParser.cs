using System.Buffers.Binary;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFullBoxHeaderParseResult
{
    Parsed,
    NeedMoreData,
}

public sealed class PlaybackIsoBmffFullBoxHeader
{
    internal PlaybackIsoBmffFullBoxHeader(byte version, uint flags)
    {
        Version = version;
        Flags = flags;
    }

    public byte Version { get; }

    public uint Flags { get; }
}

public static class PlaybackIsoBmffFullBoxHeaderParser
{
    public const int HeaderSize = 4;
    private const uint FlagsMask = 0x00ffffff;

    public static PlaybackIsoBmffFullBoxHeaderParseResult Parse(
        ReadOnlySpan<byte> payload,
        out PlaybackIsoBmffFullBoxHeader? header)
    {
        header = null;
        if (payload.Length < HeaderSize)
        {
            return PlaybackIsoBmffFullBoxHeaderParseResult.NeedMoreData;
        }

        var value = BinaryPrimitives.ReadUInt32BigEndian(payload[..HeaderSize]);
        header = new((byte)(value >> 24), value & FlagsMask);
        return PlaybackIsoBmffFullBoxHeaderParseResult.Parsed;
    }
}
