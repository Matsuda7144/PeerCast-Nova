using System.Buffers.Binary;
using System.Text;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffHandlerReferenceBoxParseResult
{
    Parsed,
    InvalidConfiguration,
    NeedMoreData,
    BoxLengthMismatch,
    WrongBoxType,
    UnsupportedVersion,
    NonzeroFlags,
    InvalidPayload,
    NameLimitExceeded,
    InvalidName,
}

public enum PlaybackIsoBmffHandlerKind
{
    Other,
    Video,
    Audio,
}

public sealed record PlaybackIsoBmffHandlerReferenceBoxParserOptions
{
    public const int DefaultMaximumNameByteCount = 255;
    public const int MaximumNameByteCountHardLimit = 4_096;

    public int MaximumNameByteCount { get; init; } = DefaultMaximumNameByteCount;
}

public sealed class PlaybackIsoBmffHandlerReferenceBox
{
    internal PlaybackIsoBmffHandlerReferenceBox(
        uint handlerType,
        PlaybackIsoBmffHandlerKind kind,
        string name)
    {
        HandlerType = handlerType;
        Kind = kind;
        Name = name;
    }

    public uint HandlerType { get; }

    public PlaybackIsoBmffHandlerKind Kind { get; }

    public string Name { get; }
}

public static class PlaybackIsoBmffHandlerReferenceBoxParser
{
    private const uint HandlerReferenceBoxType = 0x68646c72;
    private const uint VideoHandlerType = 0x76696465;
    private const uint AudioHandlerType = 0x736f756e;
    private const int FixedPayloadSize = 24;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static PlaybackIsoBmffHandlerReferenceBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffHandlerReferenceBox? handler,
        PlaybackIsoBmffHandlerReferenceBoxParserOptions? options = null)
    {
        handler = null;
        options ??= new PlaybackIsoBmffHandlerReferenceBoxParserOptions();
        if (options.MaximumNameByteCount is < 0 or
            > PlaybackIsoBmffHandlerReferenceBoxParserOptions
                .MaximumNameByteCountHardLimit)
        {
            return PlaybackIsoBmffHandlerReferenceBoxParseResult.InvalidConfiguration;
        }

        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffHandlerReferenceBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffHandlerReferenceBoxParseResult.InvalidPayload;
        }

        if (header.Type != HandlerReferenceBoxType)
        {
            return PlaybackIsoBmffHandlerReferenceBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffHandlerReferenceBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffHandlerReferenceBoxParseResult.BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length < FixedPayloadSize + 1 ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffHandlerReferenceBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version != 0)
        {
            return PlaybackIsoBmffHandlerReferenceBoxParseResult.UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffHandlerReferenceBoxParseResult.NonzeroFlags;
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4)) != 0 ||
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(12, 4)) != 0 ||
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(16, 4)) != 0 ||
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(20, 4)) != 0)
        {
            return PlaybackIsoBmffHandlerReferenceBoxParseResult.InvalidPayload;
        }

        var nameField = payload[FixedPayloadSize..];
        if (nameField.Length - 1 > options.MaximumNameByteCount)
        {
            return PlaybackIsoBmffHandlerReferenceBoxParseResult.NameLimitExceeded;
        }

        if (nameField[^1] != 0 || nameField[..^1].IndexOf((byte)0) >= 0)
        {
            return PlaybackIsoBmffHandlerReferenceBoxParseResult.InvalidName;
        }

        string name;
        try
        {
            name = StrictUtf8.GetString(nameField[..^1]);
        }
        catch (DecoderFallbackException)
        {
            return PlaybackIsoBmffHandlerReferenceBoxParseResult.InvalidName;
        }

        var handlerType = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(8, 4));
        var kind = handlerType switch
        {
            VideoHandlerType => PlaybackIsoBmffHandlerKind.Video,
            AudioHandlerType => PlaybackIsoBmffHandlerKind.Audio,
            _ => PlaybackIsoBmffHandlerKind.Other,
        };
        handler = new(handlerType, kind, name);
        return PlaybackIsoBmffHandlerReferenceBoxParseResult.Parsed;
    }
}
