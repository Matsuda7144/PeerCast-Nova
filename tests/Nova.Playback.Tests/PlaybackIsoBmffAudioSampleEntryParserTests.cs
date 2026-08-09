using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffAudioSampleEntryParserTests
{
    [Fact]
    public void Parse_ProjectsFixedFieldsAndAbsoluteChildLocations()
    {
        var box = CreateSampleDescription(
            CreateAudioPayload(Child(0x65736473, 4)));
        var selection = CreateSelection(box);

        var result = PlaybackIsoBmffAudioSampleEntryParser.Parse(
            box,
            selection,
            out var audioEntry);

        Assert.Equal(PlaybackIsoBmffAudioSampleEntryParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffAudioSampleEntry>(audioEntry);
        Assert.Same(selection, parsed.Selection);
        Assert.Equal((ushort)1, parsed.DataReferenceIndex);
        Assert.Equal((ushort)2, parsed.ChannelCount);
        Assert.Equal((ushort)16, parsed.SampleSizeBits);
        Assert.Equal((uint)0xBB800000, parsed.SampleRateFixedPoint);
        Assert.Equal((ushort)48_000, parsed.IntegerSampleRate);
        Assert.Equal((ushort)0, parsed.FractionalSampleRate);
        var child = Assert.Single(parsed.Children);
        Assert.Equal((uint)0x65736473, child.Header.Type);
        Assert.Equal(52, child.Offset);
        Assert.Equal(12, child.BoxLength);
    }

    [Fact]
    public void Parse_FractionalSampleRateIsPreserved()
    {
        var payload = CreateAudioPayload();
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(24, 4),
            0xAC441234);
        var box = CreateSampleDescription(payload);

        Assert.Equal(
            PlaybackIsoBmffAudioSampleEntryParseResult.Parsed,
            PlaybackIsoBmffAudioSampleEntryParser.Parse(
                box,
                CreateSelection(box),
                out var audioEntry));
        Assert.Equal((ushort)44_100, audioEntry!.IntegerSampleRate);
        Assert.Equal((ushort)0x1234, audioEntry.FractionalSampleRate);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(257)]
    public void Parse_InvalidChildLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffAudioSampleEntryParser.Parse(
            [],
            null,
            out var audioEntry,
            new() { MaximumChildBoxCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffAudioSampleEntryParseResult.InvalidConfiguration,
            result);
        Assert.Null(audioEntry);
    }

    [Fact]
    public void Parse_MissingAndVideoSelectionsAreRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffAudioSampleEntryParseResult.MissingSelection,
            PlaybackIsoBmffAudioSampleEntryParser.Parse(
                [],
                null,
                out var missing));
        Assert.Null(missing);

        var box = CreateSampleDescription(CreateAudioPayload(), 0x61766331);
        var entry = ParseDescription(box).Entries[0];
        var videoSelection = new PlaybackIsoBmffEditedSampleDescriptionSelection(
            1,
            entry,
            PlaybackIsoBmffSampleEntryCodecCandidate.Avc1,
            PlaybackIsoBmffHandlerKind.Video);
        Assert.Equal(
            PlaybackIsoBmffAudioSampleEntryParseResult.UnsupportedSelection,
            PlaybackIsoBmffAudioSampleEntryParser.Parse(
                box,
                videoSelection,
                out var unsupported));
        Assert.Null(unsupported);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Parse_InvalidFixedFieldsFailClosed(int mutation)
    {
        var payload = CreateAudioPayload();
        switch (mutation)
        {
            case 0:
                payload[0] = 1;
                break;
            case 1:
                BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6, 2), 0);
                break;
            case 2:
                BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(16, 2), 0);
                break;
            default:
                BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(24, 4), 0);
                break;
        }

        var box = CreateSampleDescription(payload);
        var result = PlaybackIsoBmffAudioSampleEntryParser.Parse(
            box,
            CreateSelection(box),
            out var audioEntry);

        Assert.Equal(
            PlaybackIsoBmffAudioSampleEntryParseResult.InvalidFixedFields,
            result);
        Assert.Null(audioEntry);
    }

    [Fact]
    public void Parse_StaleLocationAndInvalidChildSequenceFailClosed()
    {
        var original = CreateSampleDescription(CreateAudioPayload());
        Assert.Equal(
            PlaybackIsoBmffAudioSampleEntryParseResult.EntryLocationMismatch,
            PlaybackIsoBmffAudioSampleEntryParser.Parse(
                original.AsSpan(0, original.Length - 1),
                CreateSelection(original),
                out var stale));
        Assert.Null(stale);

        var invalid = CreateSampleDescription(
            CreateAudioPayload(new byte[7]));
        Assert.Equal(
            PlaybackIsoBmffAudioSampleEntryParseResult.InvalidChildSequence,
            PlaybackIsoBmffAudioSampleEntryParser.Parse(
                invalid,
                CreateSelection(invalid),
                out var malformed));
        Assert.Null(malformed);
    }

    [Fact]
    public void Parse_ChildCountIsBounded()
    {
        var box = CreateSampleDescription(
            CreateAudioPayload(Child(0x65736473, 0), Child(0x62747274, 0)));
        var result = PlaybackIsoBmffAudioSampleEntryParser.Parse(
            box,
            CreateSelection(box),
            out var audioEntry,
            new() { MaximumChildBoxCount = 1 });

        Assert.Equal(
            PlaybackIsoBmffAudioSampleEntryParseResult.ChildLimitExceeded,
            result);
        Assert.Null(audioEntry);
    }

    private static PlaybackIsoBmffEditedSampleDescriptionSelection
        CreateSelection(byte[] box)
    {
        var entry = ParseDescription(box).Entries[0];
        return new(
            1,
            entry,
            PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a,
            PlaybackIsoBmffHandlerKind.Audio);
    }

    private static PlaybackIsoBmffSampleDescriptionBox ParseDescription(
        byte[] box)
    {
        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.Parsed,
            PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                box,
                out var description));
        return Assert.IsType<PlaybackIsoBmffSampleDescriptionBox>(description);
    }

    internal static byte[] CreateSampleDescription(
        byte[] payload,
        uint type = 0x6D703461)
    {
        var box = new byte[24 + payload.Length];
        WriteHeader(box, 0x73747364);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(16, 4),
            (uint)(8 + payload.Length));
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), type);
        payload.CopyTo(box, 24);
        return box;
    }

    internal static byte[] CreateAudioPayload(params byte[][] children)
    {
        var payload = new byte[28 + children.Sum(static child => child.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(16, 2), 2);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(18, 2), 16);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(24, 4),
            0xBB800000);
        var offset = 28;
        foreach (var child in children)
        {
            child.CopyTo(payload, offset);
            offset += child.Length;
        }

        return payload;
    }

    private static byte[] Child(uint type, int payloadLength)
    {
        var child = new byte[8 + payloadLength];
        WriteHeader(child, type);
        return child;
    }

    private static void WriteHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4),
            (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
