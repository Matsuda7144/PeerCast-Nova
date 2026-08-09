using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffEditedSampleDescriptionSelectorTests
{
    [Theory]
    [InlineData(0x61766331, 0x76696465,
        PlaybackIsoBmffSampleEntryCodecCandidate.Avc1,
        PlaybackIsoBmffHandlerKind.Video)]
    [InlineData(0x61766333, 0x76696465,
        PlaybackIsoBmffSampleEntryCodecCandidate.Avc3,
        PlaybackIsoBmffHandlerKind.Video)]
    [InlineData(0x6D703461, 0x736F756E,
        PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a,
        PlaybackIsoBmffHandlerKind.Audio)]
    public void Select_ClassifiesSupportedEntryAgainstHandler(
        uint entryType,
        uint handlerType,
        PlaybackIsoBmffSampleEntryCodecCandidate expectedCodec,
        PlaybackIsoBmffHandlerKind expectedKind)
    {
        var result = PlaybackIsoBmffEditedSampleDescriptionSelector.Select(
            CreateEditedRange(1),
            ParseSampleDescription(entryType),
            ParseHandler(handlerType),
            out var selection);

        Assert.Equal(
            PlaybackIsoBmffEditedSampleDescriptionSelectionResult.Selected,
            result);
        var selected = Assert.IsType<
            PlaybackIsoBmffEditedSampleDescriptionSelection>(selection);
        Assert.Equal((uint)1, selected.SampleDescriptionIndex);
        Assert.Equal(entryType, selected.Entry.Header.Type);
        Assert.Equal(expectedCodec, selected.CodecCandidate);
        Assert.Equal(expectedKind, selected.MediaKind);
    }

    [Fact]
    public void Select_UsesBoundOneBasedDescriptionIndex()
    {
        var result = PlaybackIsoBmffEditedSampleDescriptionSelector.Select(
            CreateEditedRange(2),
            ParseSampleDescription(0x6D703461, 0x61766331),
            ParseHandler(0x76696465),
            out var selection);

        Assert.Equal(
            PlaybackIsoBmffEditedSampleDescriptionSelectionResult.Selected,
            result);
        Assert.Equal(
            PlaybackIsoBmffSampleEntryCodecCandidate.Avc1,
            selection!.CodecCandidate);
    }

    [Fact]
    public void Select_UnsupportedEntryAndHandlerMismatchAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffEditedSampleDescriptionSelectionResult
                .UnsupportedSampleEntry,
            PlaybackIsoBmffEditedSampleDescriptionSelector.Select(
                CreateEditedRange(1),
                ParseSampleDescription(0x77767031),
                ParseHandler(0x76696465),
                out var unsupported));
        Assert.Null(unsupported);

        Assert.Equal(
            PlaybackIsoBmffEditedSampleDescriptionSelectionResult
                .HandlerMismatch,
            PlaybackIsoBmffEditedSampleDescriptionSelector.Select(
                CreateEditedRange(1),
                ParseSampleDescription(0x61766331),
                ParseHandler(0x736F756E),
                out var mismatch));
        Assert.Null(mismatch);
    }

    [Fact]
    public void Select_DescriptionIndexMustBeInRange()
    {
        var result = PlaybackIsoBmffEditedSampleDescriptionSelector.Select(
            CreateEditedRange(2),
            ParseSampleDescription(0x61766331),
            ParseHandler(0x76696465),
            out var selection);

        Assert.Equal(
            PlaybackIsoBmffEditedSampleDescriptionSelectionResult
                .SampleDescriptionIndexOutOfRange,
            result);
        Assert.Null(selection);
    }

    [Fact]
    public void Select_MissingInputsAreDistinct()
    {
        var range = CreateEditedRange(1);
        var description = ParseSampleDescription(0x61766331);
        var handler = ParseHandler(0x76696465);

        AssertMissing(
            PlaybackIsoBmffEditedSampleDescriptionSelectionResult
                .MissingEditedRange,
            null,
            description,
            handler);
        AssertMissing(
            PlaybackIsoBmffEditedSampleDescriptionSelectionResult
                .MissingSampleDescription,
            range,
            null,
            handler);
        AssertMissing(
            PlaybackIsoBmffEditedSampleDescriptionSelectionResult
                .MissingHandler,
            range,
            description,
            null);
    }

    private static void AssertMissing(
        PlaybackIsoBmffEditedSampleDescriptionSelectionResult expected,
        PlaybackIsoBmffEditedSampleByteRange? range,
        PlaybackIsoBmffSampleDescriptionBox? description,
        PlaybackIsoBmffHandlerReferenceBox? handler)
    {
        var result = PlaybackIsoBmffEditedSampleDescriptionSelector.Select(
            range,
            description,
            handler,
            out var selection);
        Assert.Equal(expected, result);
        Assert.Null(selection);
    }

    private static PlaybackIsoBmffEditedSampleByteRange CreateEditedRange(
        uint sampleDescriptionIndex)
    {
        var sample = new PlaybackIsoBmffSamplePresentationTime(0, 1, 1, 0,
            false);
        var appearances = Array.AsReadOnly(
            new[]
            {
                new PlaybackIsoBmffSampleEditMovieOverlap(
                    0,
                    0,
                    1,
                    1,
                    true),
            });
        var admission = new PlaybackIsoBmffEditedSampleAdmission(
            1,
            new PlaybackIsoBmffCompleteSampleEditAdmission(sample, appearances));
        return new(
            admission,
            new(8, 1, 9),
            new(0, 1, 1, 0, 1, sampleDescriptionIndex));
    }

    internal static PlaybackIsoBmffSampleDescriptionBox ParseSampleDescription(
        params uint[] entryTypes)
    {
        var box = new byte[16 + (entryTypes.Length * 8)];
        WriteHeader(box, 0x73747364);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4),
            (uint)entryTypes.Length);
        for (var index = 0; index < entryTypes.Length; index++)
        {
            var offset = 16 + (index * 8);
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(offset, 4), 8);
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(offset + 4, 4),
                entryTypes[index]);
        }

        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.Parsed,
            PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                box,
                out var description));
        return Assert.IsType<PlaybackIsoBmffSampleDescriptionBox>(description);
    }

    internal static PlaybackIsoBmffHandlerReferenceBox ParseHandler(uint type)
    {
        var box = new byte[33];
        WriteHeader(box, 0x68646C72);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(16, 4), type);
        Assert.Equal(
            PlaybackIsoBmffHandlerReferenceBoxParseResult.Parsed,
            PlaybackIsoBmffHandlerReferenceBoxParser.Parse(box, out var handler));
        return Assert.IsType<PlaybackIsoBmffHandlerReferenceBox>(handler);
    }

    private static void WriteHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4),
            (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
