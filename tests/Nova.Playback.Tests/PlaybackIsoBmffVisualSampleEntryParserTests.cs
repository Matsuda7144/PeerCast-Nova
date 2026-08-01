using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffVisualSampleEntryParserTests
{
    [Fact]
    public void Parse_ProjectsFixedFieldsAndAbsoluteChildLocations()
    {
        var box = CreateSampleDescription(
            0x61766331,
            CreateVisualPayload(Child(0x61766343, 4)));
        var selection = CreateSelection(box,
            PlaybackIsoBmffSampleEntryCodecCandidate.Avc1);

        var result = PlaybackIsoBmffVisualSampleEntryParser.Parse(
            box,
            selection,
            out var visualEntry);

        Assert.Equal(PlaybackIsoBmffVisualSampleEntryParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffVisualSampleEntry>(visualEntry);
        Assert.Same(selection, parsed.Selection);
        Assert.Equal((ushort)1, parsed.DataReferenceIndex);
        Assert.Equal((ushort)1920, parsed.Width);
        Assert.Equal((ushort)1080, parsed.Height);
        Assert.Equal((uint)0x00480000, parsed.HorizontalResolutionFixedPoint);
        Assert.Equal((uint)0x00480000, parsed.VerticalResolutionFixedPoint);
        Assert.Equal((ushort)1, parsed.FrameCount);
        Assert.Equal(new byte[] { (byte)'N', (byte)'o', (byte)'v', (byte)'a' },
            parsed.CompressorName.ToArray());
        Assert.Equal((ushort)0x0018, parsed.Depth);
        var child = Assert.Single(parsed.Children);
        Assert.Equal((uint)0x61766343, child.Header.Type);
        Assert.Equal(102, child.Offset);
        Assert.Equal(12, child.BoxLength);
    }

    [Theory]
    [InlineData(0x61766331,
        PlaybackIsoBmffSampleEntryCodecCandidate.Avc1)]
    [InlineData(0x61766333,
        PlaybackIsoBmffSampleEntryCodecCandidate.Avc3)]
    public void Parse_AvcEntryCandidatesAreSupported(
        uint type,
        PlaybackIsoBmffSampleEntryCodecCandidate candidate)
    {
        var box = CreateSampleDescription(type, CreateVisualPayload());

        Assert.Equal(
            PlaybackIsoBmffVisualSampleEntryParseResult.Parsed,
            PlaybackIsoBmffVisualSampleEntryParser.Parse(
                box,
                CreateSelection(box, candidate),
                out var visualEntry));
        Assert.Empty(visualEntry!.Children);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(257)]
    public void Parse_InvalidChildLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffVisualSampleEntryParser.Parse(
            [],
            null,
            out var visualEntry,
            new() { MaximumChildBoxCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffVisualSampleEntryParseResult.InvalidConfiguration,
            result);
        Assert.Null(visualEntry);
    }

    [Fact]
    public void Parse_MissingAndAudioSelectionsAreRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffVisualSampleEntryParseResult.MissingSelection,
            PlaybackIsoBmffVisualSampleEntryParser.Parse(
                [],
                null,
                out var missing));
        Assert.Null(missing);

        var box = CreateSampleDescription(0x6D703461, CreateVisualPayload());
        var entry = ParseDescription(box).Entries[0];
        var audioSelection = new PlaybackIsoBmffEditedSampleDescriptionSelection(
            1,
            entry,
            PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a,
            PlaybackIsoBmffHandlerKind.Audio);
        Assert.Equal(
            PlaybackIsoBmffVisualSampleEntryParseResult.UnsupportedSelection,
            PlaybackIsoBmffVisualSampleEntryParser.Parse(
                box,
                audioSelection,
                out var unsupported));
        Assert.Null(unsupported);
    }

    [Fact]
    public void Parse_StaleEntryLocationFailsClosed()
    {
        var original = CreateSampleDescription(
            0x61766331,
            CreateVisualPayload());
        var changed = original[..^1];

        var result = PlaybackIsoBmffVisualSampleEntryParser.Parse(
            changed,
            CreateSelection(original,
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc1),
            out var visualEntry);

        Assert.Equal(
            PlaybackIsoBmffVisualSampleEntryParseResult.EntryLocationMismatch,
            result);
        Assert.Null(visualEntry);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Parse_InvalidFixedFieldsFailClosed(int mutation)
    {
        var payload = CreateVisualPayload();
        switch (mutation)
        {
            case 0:
                payload[0] = 1;
                break;
            case 1:
                BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6, 2), 0);
                break;
            case 2:
                payload[42] = 32;
                break;
            default:
                BinaryPrimitives.WriteInt16BigEndian(payload.AsSpan(76, 2), 0);
                break;
        }

        var box = CreateSampleDescription(0x61766331, payload);
        var result = PlaybackIsoBmffVisualSampleEntryParser.Parse(
            box,
            CreateSelection(box,
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc1),
            out var visualEntry);

        Assert.Equal(
            PlaybackIsoBmffVisualSampleEntryParseResult.InvalidFixedFields,
            result);
        Assert.Null(visualEntry);
    }

    [Fact]
    public void Parse_ChildCountAndSequenceAreBounded()
    {
        var twoChildren = CreateSampleDescription(
            0x61766331,
            CreateVisualPayload(Child(0x61766343, 0), Child(0x70617370, 0)));
        Assert.Equal(
            PlaybackIsoBmffVisualSampleEntryParseResult.ChildLimitExceeded,
            PlaybackIsoBmffVisualSampleEntryParser.Parse(
                twoChildren,
                CreateSelection(twoChildren,
                    PlaybackIsoBmffSampleEntryCodecCandidate.Avc1),
                out var limited,
                new() { MaximumChildBoxCount = 1 }));
        Assert.Null(limited);

        var invalidPayload = CreateVisualPayload(new byte[7]);
        var invalid = CreateSampleDescription(0x61766331, invalidPayload);
        Assert.Equal(
            PlaybackIsoBmffVisualSampleEntryParseResult.InvalidChildSequence,
            PlaybackIsoBmffVisualSampleEntryParser.Parse(
                invalid,
                CreateSelection(invalid,
                    PlaybackIsoBmffSampleEntryCodecCandidate.Avc1),
                out var malformed));
        Assert.Null(malformed);
    }

    private static PlaybackIsoBmffEditedSampleDescriptionSelection
        CreateSelection(
            byte[] box,
            PlaybackIsoBmffSampleEntryCodecCandidate candidate)
    {
        var entry = ParseDescription(box).Entries[0];
        return new(1, entry, candidate, PlaybackIsoBmffHandlerKind.Video);
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

    private static byte[] CreateSampleDescription(uint type, byte[] payload)
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

    private static byte[] CreateVisualPayload(params byte[][] children)
    {
        var payload = new byte[78 + children.Sum(static child => child.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(24, 2), 1920);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(26, 2), 1080);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(28, 4), 0x00480000);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(32, 4), 0x00480000);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(40, 2), 1);
        payload[42] = 4;
        "Nova"u8.CopyTo(payload.AsSpan(43));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(74, 2), 0x0018);
        BinaryPrimitives.WriteInt16BigEndian(payload.AsSpan(76, 2), -1);
        var offset = 78;
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
