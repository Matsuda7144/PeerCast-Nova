using System.Buffers.Binary;
using System.Text;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffHandlerReferenceBoxParserTests
{
    private const uint Hdlr = 0x68646c72;
    private const uint Mdia = 0x6d646961;
    private const uint Vide = 0x76696465;
    private const uint Soun = 0x736f756e;
    private const uint Meta = 0x6d657461;

    [Theory]
    [InlineData(Vide, PlaybackIsoBmffHandlerKind.Video, "VideoHandler")]
    [InlineData(Soun, PlaybackIsoBmffHandlerKind.Audio, "Audio Handler")]
    [InlineData(Meta, PlaybackIsoBmffHandlerKind.Other, "Metadata")]
    public void Parse_ProjectsHandlerTypeKindAndUtf8Name(
        uint handlerType,
        PlaybackIsoBmffHandlerKind kind,
        string name)
    {
        var box = CreateBox(handlerType, name);

        var result = PlaybackIsoBmffHandlerReferenceBoxParser.Parse(
            box,
            out var handler);

        Assert.Equal(PlaybackIsoBmffHandlerReferenceBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffHandlerReferenceBox>(handler);
        Assert.Equal(handlerType, parsed.HandlerType);
        Assert.Equal(kind, parsed.Kind);
        Assert.Equal(name, parsed.Name);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        var extended = CreateBox(Vide, "映像", extended: true);
        var toEnd = CreateBox(Soun, string.Empty, extendsToEnd: true);

        var extendedResult = PlaybackIsoBmffHandlerReferenceBoxParser.Parse(
            extended,
            out var extendedHandler);
        var toEndResult = PlaybackIsoBmffHandlerReferenceBoxParser.Parse(
            toEnd,
            out var toEndHandler);

        Assert.Equal(
            PlaybackIsoBmffHandlerReferenceBoxParseResult.Parsed,
            extendedResult);
        Assert.Equal(
            "映像",
            Assert.IsType<PlaybackIsoBmffHandlerReferenceBox>(
                extendedHandler).Name);
        Assert.Equal(
            PlaybackIsoBmffHandlerReferenceBoxParseResult.Parsed,
            toEndResult);
        Assert.Equal(
            string.Empty,
            Assert.IsType<PlaybackIsoBmffHandlerReferenceBox>(toEndHandler).Name);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4097)]
    public void Parse_InvalidNameLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffHandlerReferenceBoxParser.Parse(
            [],
            out var handler,
            new() { MaximumNameByteCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffHandlerReferenceBoxParseResult.InvalidConfiguration,
            result);
        Assert.Null(handler);
    }

    [Fact]
    public void Parse_NameLimitCountsUtf8BytesBeforeDecoding()
    {
        var box = CreateBox(Vide, "映像");

        var result = PlaybackIsoBmffHandlerReferenceBoxParser.Parse(
            box,
            out var handler,
            new() { MaximumNameByteCount = 5 });

        Assert.Equal(
            PlaybackIsoBmffHandlerReferenceBoxParseResult.NameLimitExceeded,
            result);
        Assert.Null(handler);
    }

    [Fact]
    public void Parse_InvalidUtf8NameIsRejected()
    {
        var box = CreateBox(Vide, "x");
        box[^2] = 0xff;

        var result = PlaybackIsoBmffHandlerReferenceBoxParser.Parse(
            box,
            out var handler);

        Assert.Equal(
            PlaybackIsoBmffHandlerReferenceBoxParseResult.InvalidName,
            result);
        Assert.Null(handler);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parse_MissingOrEmbeddedTerminatorIsRejected(bool embedded)
    {
        var box = CreateBox(Vide, "name");
        if (embedded)
        {
            box[^3] = 0;
        }
        else
        {
            box[^1] = 1;
        }

        var result = PlaybackIsoBmffHandlerReferenceBoxParser.Parse(
            box,
            out var handler);

        Assert.Equal(
            PlaybackIsoBmffHandlerReferenceBoxParseResult.InvalidName,
            result);
        Assert.Null(handler);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    public void Parse_NonzeroReservedFieldIsRejected(int payloadOffset)
    {
        var box = CreateBox(Vide, "name");
        box[8 + payloadOffset + 3] = 1;

        var result = PlaybackIsoBmffHandlerReferenceBoxParser.Parse(
            box,
            out var handler);

        Assert.Equal(
            PlaybackIsoBmffHandlerReferenceBoxParseResult.InvalidPayload,
            result);
        Assert.Null(handler);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndNonzeroFlagsAreDistinct()
    {
        var version = CreateBox(Vide, "name");
        version[8] = 1;
        var flags = CreateBox(Vide, "name");
        flags[11] = 1;

        var versionResult = PlaybackIsoBmffHandlerReferenceBoxParser.Parse(
            version,
            out var versionHandler);
        var flagsResult = PlaybackIsoBmffHandlerReferenceBoxParser.Parse(
            flags,
            out var flagsHandler);

        Assert.Equal(
            PlaybackIsoBmffHandlerReferenceBoxParseResult.UnsupportedVersion,
            versionResult);
        Assert.Null(versionHandler);
        Assert.Equal(
            PlaybackIsoBmffHandlerReferenceBoxParseResult.NonzeroFlags,
            flagsResult);
        Assert.Null(flagsHandler);
    }

    [Fact]
    public void Parse_WrongBoxTypeIsRejected()
    {
        var box = CreateBox(Vide, "name");
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Mdia);

        var result = PlaybackIsoBmffHandlerReferenceBoxParser.Parse(
            box,
            out var handler);

        Assert.Equal(
            PlaybackIsoBmffHandlerReferenceBoxParseResult.WrongBoxType,
            result);
        Assert.Null(handler);
    }

    [Fact]
    public void Parse_DeclaredLengthMustMatchCompleteInput()
    {
        var shortInput = CreateBox(Vide, "name");
        BinaryPrimitives.WriteUInt32BigEndian(
            shortInput.AsSpan(0, 4),
            (uint)shortInput.Length + 1);
        var original = CreateBox(Vide, "name");
        var trailingInput = new byte[original.Length + 1];
        original.CopyTo(trailingInput, 0);

        var shortResult = PlaybackIsoBmffHandlerReferenceBoxParser.Parse(
            shortInput,
            out var shortHandler);
        var trailingResult = PlaybackIsoBmffHandlerReferenceBoxParser.Parse(
            trailingInput,
            out var trailingHandler);

        Assert.Equal(
            PlaybackIsoBmffHandlerReferenceBoxParseResult.NeedMoreData,
            shortResult);
        Assert.Null(shortHandler);
        Assert.Equal(
            PlaybackIsoBmffHandlerReferenceBoxParseResult.BoxLengthMismatch,
            trailingResult);
        Assert.Null(trailingHandler);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(32)]
    public void Parse_TruncatedHeaderOrFixedPayloadIsRejected(int length)
    {
        var box = new byte[length];
        if (length >= 8)
        {
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)length);
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Hdlr);
        }

        var result = PlaybackIsoBmffHandlerReferenceBoxParser.Parse(
            box,
            out var handler);

        var expected = length < 8
            ? PlaybackIsoBmffHandlerReferenceBoxParseResult.NeedMoreData
            : PlaybackIsoBmffHandlerReferenceBoxParseResult.InvalidPayload;
        Assert.Equal(expected, result);
        Assert.Null(handler);
    }

    private static byte[] CreateBox(
        uint handlerType,
        string name,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var box = new byte[headerSize + 24 + nameBytes.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Hdlr);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8, 8), (ulong)box.Length);
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 8, 4),
            handlerType);
        nameBytes.CopyTo(box.AsSpan(headerSize + 24));
        return box;
    }
}
