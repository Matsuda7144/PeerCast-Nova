using System.Buffers.Binary;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffBoxHeaderParseResult
{
    Parsed,
    NeedMoreData,
    InvalidSize,
}

public readonly record struct PlaybackIsoBmffUserType(ulong High, ulong Low);

public sealed class PlaybackIsoBmffBoxHeader
{
    internal PlaybackIsoBmffBoxHeader(
        uint type,
        ulong? boxSize,
        int headerSize,
        PlaybackIsoBmffUserType? userType)
    {
        Type = type;
        BoxSize = boxSize;
        HeaderSize = headerSize;
        UserType = userType;
    }

    public uint Type { get; }

    public ulong? BoxSize { get; }

    public int HeaderSize { get; }

    public ulong? PayloadSize =>
        BoxSize is { } boxSize ? boxSize - (ulong)HeaderSize : null;

    public bool ExtendsToEnd => BoxSize is null;

    public PlaybackIsoBmffUserType? UserType { get; }
}

public static class PlaybackIsoBmffBoxHeaderParser
{
    private const int BaseHeaderSize = 8;
    private const int ExtendedSizeHeaderSize = 16;
    private const int UserTypeSize = 16;
    private const uint ExtendedSizeMarker = 1;
    private const uint ExtendsToEndMarker = 0;
    private const uint UuidType = 0x75756964;

    public static PlaybackIsoBmffBoxHeaderParseResult Parse(
        ReadOnlySpan<byte> input,
        out PlaybackIsoBmffBoxHeader? header)
    {
        header = null;
        if (input.Length < BaseHeaderSize)
        {
            return PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData;
        }

        var size32 = BinaryPrimitives.ReadUInt32BigEndian(input[..4]);
        var type = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(4, 4));
        var baseHeaderSize = BaseHeaderSize;
        ulong? boxSize = size32;
        if (size32 == ExtendedSizeMarker)
        {
            baseHeaderSize = ExtendedSizeHeaderSize;
            if (input.Length < baseHeaderSize)
            {
                return PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData;
            }

            boxSize = BinaryPrimitives.ReadUInt64BigEndian(input.Slice(8, 8));
        }
        else if (size32 == ExtendsToEndMarker)
        {
            boxSize = null;
        }

        var headerSize = baseHeaderSize + (type == UuidType ? UserTypeSize : 0);
        if (boxSize is { } declaredSize && declaredSize < (ulong)headerSize)
        {
            return PlaybackIsoBmffBoxHeaderParseResult.InvalidSize;
        }

        if (input.Length < headerSize)
        {
            return PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData;
        }

        PlaybackIsoBmffUserType? userType = null;
        if (type == UuidType)
        {
            userType = new(
                BinaryPrimitives.ReadUInt64BigEndian(
                    input.Slice(baseHeaderSize, sizeof(ulong))),
                BinaryPrimitives.ReadUInt64BigEndian(
                    input.Slice(baseHeaderSize + sizeof(ulong), sizeof(ulong))));
        }

        header = new(type, boxSize, headerSize, userType);
        return PlaybackIsoBmffBoxHeaderParseResult.Parsed;
    }
}
