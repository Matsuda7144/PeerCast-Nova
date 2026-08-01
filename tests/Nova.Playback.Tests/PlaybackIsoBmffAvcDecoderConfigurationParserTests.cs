using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffAvcDecoderConfigurationParserTests
{
    [Fact]
    public void Parse_ProjectsBaselineConfigurationAndOwnedParameterSets()
    {
        var record = BaselineRecord();
        var (box, visualEntry) = CreateVisualEntry(record);

        var result = PlaybackIsoBmffAvcDecoderConfigurationParser.Parse(
            box,
            visualEntry,
            out var configuration);

        Assert.Equal(
            PlaybackIsoBmffAvcDecoderConfigurationParseResult.Parsed,
            result);
        var parsed = Assert.IsType<PlaybackIsoBmffAvcDecoderConfiguration>(
            configuration);
        Assert.Same(visualEntry, parsed.VisualEntry);
        Assert.Equal((byte)66, parsed.ProfileIndication);
        Assert.Equal((byte)0, parsed.ProfileCompatibility);
        Assert.Equal((byte)30, parsed.LevelIndication);
        Assert.Equal((byte)4, parsed.NalUnitLengthBytes);
        Assert.Null(parsed.ChromaFormat);
        Assert.Equal(2, parsed.ParameterSets.Count);
        Assert.Equal(PlaybackIsoBmffAvcParameterSetKind.Sequence,
            parsed.ParameterSets[0].Kind);
        Assert.Equal(new byte[] { 0x67, 0x42 },
            parsed.ParameterSets[0].Payload.ToArray());
        Assert.Equal(PlaybackIsoBmffAvcParameterSetKind.Picture,
            parsed.ParameterSets[1].Kind);
        Assert.Equal(new byte[] { 0x68 },
            parsed.ParameterSets[1].Payload.ToArray());
        Assert.Equal(record, parsed.ConfigurationRecord.ToArray());

        record[8] = 0;
        Assert.Equal((byte)0x67, parsed.ParameterSets[0].Payload.Span[0]);
    }

    [Fact]
    public void Parse_ProjectsHighProfileExtension()
    {
        var (box, visualEntry) = CreateVisualEntry(HighProfileRecord());

        Assert.Equal(
            PlaybackIsoBmffAvcDecoderConfigurationParseResult.Parsed,
            PlaybackIsoBmffAvcDecoderConfigurationParser.Parse(
                box,
                visualEntry,
                out var configuration));
        Assert.Equal((byte)1, configuration!.ChromaFormat);
        Assert.Equal((byte)0, configuration.BitDepthLumaMinus8);
        Assert.Equal((byte)0, configuration.BitDepthChromaMinus8);
        Assert.Equal(3, configuration.ParameterSets.Count);
        Assert.Equal(PlaybackIsoBmffAvcParameterSetKind.SequenceExtension,
            configuration.ParameterSets[2].Kind);
    }

    [Fact]
    public void Parse_MissingAndDuplicateConfigurationBoxesAreDistinct()
    {
        var (missingBox, missingEntry) = CreateVisualEntry();
        Assert.Equal(
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .MissingConfigurationBox,
            PlaybackIsoBmffAvcDecoderConfigurationParser.Parse(
                missingBox,
                missingEntry,
                out var missing));
        Assert.Null(missing);

        var (duplicateBox, duplicateEntry) = CreateVisualEntry(
            BaselineRecord(),
            BaselineRecord());
        Assert.Equal(
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .DuplicateConfigurationBox,
            PlaybackIsoBmffAvcDecoderConfigurationParser.Parse(
                duplicateBox,
                duplicateEntry,
                out var duplicate));
        Assert.Null(duplicate);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4_097, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1_048_577)]
    public void Parse_InvalidLimitsRejectBeforeInputAccess(
        int maximumSets,
        int maximumBytes)
    {
        var result = PlaybackIsoBmffAvcDecoderConfigurationParser.Parse(
            [],
            null,
            out var configuration,
            new()
            {
                MaximumParameterSetCount = maximumSets,
                MaximumConfigurationBytes = maximumBytes,
            });

        Assert.Equal(
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .InvalidConfiguration,
            result);
        Assert.Null(configuration);
    }

    [Fact]
    public void Parse_ConfigurationAndParameterSetLimitsAreEnforced()
    {
        var (box, visualEntry) = CreateVisualEntry(BaselineRecord());
        Assert.Equal(
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .ConfigurationLimitExceeded,
            PlaybackIsoBmffAvcDecoderConfigurationParser.Parse(
                box,
                visualEntry,
                out var oversized,
                new() { MaximumConfigurationBytes = 8 }));
        Assert.Null(oversized);

        Assert.Equal(
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .ParameterSetLimitExceeded,
            PlaybackIsoBmffAvcDecoderConfigurationParser.Parse(
                box,
                visualEntry,
                out var tooMany,
                new() { MaximumParameterSetCount = 1 }));
        Assert.Null(tooMany);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Parse_MalformedConfigurationFailsClosed(int mutation)
    {
        var record = BaselineRecord();
        switch (mutation)
        {
            case 0:
                record[0] = 2;
                break;
            case 1:
                record[4] = 0;
                break;
            case 2:
                record[6] = 0;
                record[7] = 0;
                break;
            default:
                record[^1] = 0;
                record[12] = 2;
                break;
        }

        var (box, visualEntry) = CreateVisualEntry(record);
        var result = PlaybackIsoBmffAvcDecoderConfigurationParser.Parse(
            box,
            visualEntry,
            out var configuration);

        Assert.Equal(
            PlaybackIsoBmffAvcDecoderConfigurationParseResult.InvalidPayload,
            result);
        Assert.Null(configuration);
    }

    [Fact]
    public void Parse_StaleConfigurationLocationFailsClosed()
    {
        var (box, visualEntry) = CreateVisualEntry(BaselineRecord());
        var changed = box.ToArray();
        changed[visualEntry.Children[0].Offset + 4] = (byte)'x';

        var result = PlaybackIsoBmffAvcDecoderConfigurationParser.Parse(
            changed,
            visualEntry,
            out var configuration);

        Assert.Equal(
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .ConfigurationLocationMismatch,
            result);
        Assert.Null(configuration);
    }

    private static (byte[] Box, PlaybackIsoBmffVisualSampleEntry Entry)
        CreateVisualEntry(params byte[][] configurationRecords)
    {
        var children = configurationRecords.Select(record =>
        {
            var child = new byte[8 + record.Length];
            WriteHeader(child, 0x61766343);
            record.CopyTo(child, 8);
            return child;
        }).ToArray();
        var payload = new byte[78 + children.Sum(child => child.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(24, 2), 640);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(26, 2), 360);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(40, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(74, 2), 0x18);
        BinaryPrimitives.WriteInt16BigEndian(payload.AsSpan(76, 2), -1);
        var childOffset = 78;
        foreach (var child in children)
        {
            child.CopyTo(payload, childOffset);
            childOffset += child.Length;
        }

        var box = new byte[24 + payload.Length];
        WriteHeader(box, 0x73747364);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(16, 4),
            (uint)(8 + payload.Length));
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), 0x61766331);
        payload.CopyTo(box, 24);
        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.Parsed,
            PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                box,
                out var description));
        var selection = new PlaybackIsoBmffEditedSampleDescriptionSelection(
            1,
            description!.Entries[0],
            PlaybackIsoBmffSampleEntryCodecCandidate.Avc1,
            PlaybackIsoBmffHandlerKind.Video);
        Assert.Equal(
            PlaybackIsoBmffVisualSampleEntryParseResult.Parsed,
            PlaybackIsoBmffVisualSampleEntryParser.Parse(
                box,
                selection,
                out var visualEntry));
        return (box, Assert.IsType<PlaybackIsoBmffVisualSampleEntry>(visualEntry));
    }

    private static byte[] BaselineRecord() =>
    [
        1, 66, 0, 30, 0xFF, 0xE1,
        0, 2, 0x67, 0x42,
        1, 0, 1, 0x68,
    ];

    private static byte[] HighProfileRecord() =>
    [
        1, 100, 0, 40, 0xFF, 0xE1,
        0, 2, 0x67, 0x64,
        1, 0, 1, 0x68,
        0xFD, 0xF8, 0xF8, 1,
        0, 1, 0x6D,
    ];

    private static void WriteHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4),
            (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
