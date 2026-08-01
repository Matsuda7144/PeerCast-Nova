using System.Buffers.Binary;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMediaHeaderTimingBoxParseResult
{
    Parsed,
    NeedMoreData,
    BoxLengthMismatch,
    WrongBoxType,
    UnsupportedVersion,
    NonzeroFlags,
    InvalidPayload,
    InvalidTimescale,
    InvalidLanguage,
}

public sealed class PlaybackIsoBmffMediaHeaderTimingBox
{
    internal PlaybackIsoBmffMediaHeaderTimingBox(
        byte version,
        ulong creationTime,
        ulong modificationTime,
        uint timescale,
        ulong? durationUnits,
        string language)
    {
        Version = version;
        CreationTime = creationTime;
        ModificationTime = modificationTime;
        Timescale = timescale;
        DurationUnits = durationUnits;
        Language = language;
    }

    public byte Version { get; }

    public ulong CreationTime { get; }

    public ulong ModificationTime { get; }

    public uint Timescale { get; }

    public ulong? DurationUnits { get; }

    public bool HasKnownDuration => DurationUnits is not null;

    public string Language { get; }
}

public static class PlaybackIsoBmffMediaHeaderTimingBoxParser
{
    private const uint MediaHeaderBoxType = 0x6d646864;
    private const int Version0PayloadSize = 24;
    private const int Version1PayloadSize = 36;
    private const ushort ReservedLanguageBit = 0x8000;

    public static PlaybackIsoBmffMediaHeaderTimingBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffMediaHeaderTimingBox? timing)
    {
        timing = null;
        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffMediaHeaderTimingBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffMediaHeaderTimingBoxParseResult.InvalidPayload;
        }

        if (header.Type != MediaHeaderBoxType)
        {
            return PlaybackIsoBmffMediaHeaderTimingBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffMediaHeaderTimingBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffMediaHeaderTimingBoxParseResult.BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffMediaHeaderTimingBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version is not 0 and not 1)
        {
            return PlaybackIsoBmffMediaHeaderTimingBoxParseResult.UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffMediaHeaderTimingBoxParseResult.NonzeroFlags;
        }

        var expectedPayloadSize = fullBox.Version == 0
            ? Version0PayloadSize
            : Version1PayloadSize;
        if (payload.Length != expectedPayloadSize)
        {
            return PlaybackIsoBmffMediaHeaderTimingBoxParseResult.InvalidPayload;
        }

        ulong creationTime;
        ulong modificationTime;
        uint timescale;
        ulong duration;
        ulong unknownDuration;
        int languageOffset;
        if (fullBox.Version == 0)
        {
            creationTime = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
            modificationTime = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(8, 4));
            timescale = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(12, 4));
            duration = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(16, 4));
            unknownDuration = uint.MaxValue;
            languageOffset = 20;
        }
        else
        {
            creationTime = BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(4, 8));
            modificationTime = BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(12, 8));
            timescale = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(20, 4));
            duration = BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(24, 8));
            unknownDuration = ulong.MaxValue;
            languageOffset = 32;
        }

        if (timescale == 0)
        {
            return PlaybackIsoBmffMediaHeaderTimingBoxParseResult.InvalidTimescale;
        }

        var packedLanguage = BinaryPrimitives.ReadUInt16BigEndian(
            payload.Slice(languageOffset, 2));
        if ((packedLanguage & ReservedLanguageBit) != 0 ||
            !TryDecodeLanguage(packedLanguage, out var language))
        {
            return PlaybackIsoBmffMediaHeaderTimingBoxParseResult.InvalidLanguage;
        }

        if (BinaryPrimitives.ReadUInt16BigEndian(
            payload.Slice(languageOffset + 2, 2)) != 0)
        {
            return PlaybackIsoBmffMediaHeaderTimingBoxParseResult.InvalidPayload;
        }

        timing = new(
            fullBox.Version,
            creationTime,
            modificationTime,
            timescale,
            duration == unknownDuration ? null : duration,
            language);
        return PlaybackIsoBmffMediaHeaderTimingBoxParseResult.Parsed;
    }

    private static bool TryDecodeLanguage(ushort packed, out string language)
    {
        Span<char> characters = stackalloc char[3];
        for (var index = 0; index < characters.Length; index++)
        {
            var shift = (2 - index) * 5;
            var value = (packed >> shift) & 0x1f;
            if (value is < 1 or > 26)
            {
                language = string.Empty;
                return false;
            }

            characters[index] = (char)('a' + value - 1);
        }

        language = new string(characters);
        return true;
    }
}
