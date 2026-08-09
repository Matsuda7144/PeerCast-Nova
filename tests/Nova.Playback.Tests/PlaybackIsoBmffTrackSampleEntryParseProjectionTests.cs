using Result = PeerCast.Nova.Playback.PlaybackIsoBmffTrackSampleEntryParseProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackSampleEntryParseProjectionTests
{
    [Fact]
    public void Project_ParsesExactAudioEntriesInSourceOrder()
    {
        var box = PlaybackIsoBmffAudioSampleEntryParserTests
            .CreateSampleDescription(
                PlaybackIsoBmffAudioSampleEntryParserTests
                    .CreateAudioPayload());
        var source = Source(box, 0x736F756E);

        var projection = Project(box, source);
        Assert.Same(source, projection.Source);
        Assert.Equal(box.Length, projection.SampleDescriptionBoxLength);
        Assert.Equal(64, projection.MaximumChildBoxCount);
        Assert.Equal(source.Entries.Count, projection.Entries.Count);
        for (var index = 0; index < projection.Entries.Count; index++)
        {
            var entry = projection.Entries[index];
            Assert.Same(source.Entries[index], entry.Source);
            Assert.Equal(source.Entries[index].SampleNumber,
                entry.SampleNumber);
            Assert.Equal(PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a,
                entry.CodecCandidate);
            Assert.Null(entry.VisualEntry);
            Assert.Same(source.Entries[index].Selection,
                Assert.IsType<PlaybackIsoBmffAudioSampleEntry>(entry.AudioEntry)
                    .Selection);
        }
    }

    [Theory]
    [InlineData(0x61766331U,
        PlaybackIsoBmffSampleEntryCodecCandidate.Avc1)]
    [InlineData(0x61766333U,
        PlaybackIsoBmffSampleEntryCodecCandidate.Avc3)]
    public void Project_ParsesExactVisualEntries(
        uint entryType,
        PlaybackIsoBmffSampleEntryCodecCandidate codecCandidate)
    {
        var box = PlaybackIsoBmffVisualSampleEntryParserTests
            .CreateSampleDescription(
                entryType,
                PlaybackIsoBmffVisualSampleEntryParserTests
                    .CreateVisualPayload());
        var source = Source(box, 0x76696465);

        var projection = Project(box, source);
        Assert.All(projection.Entries, entry =>
        {
            Assert.Equal(codecCandidate, entry.CodecCandidate);
            Assert.Null(entry.AudioEntry);
            Assert.Same(entry.Source.Selection,
                Assert.IsType<PlaybackIsoBmffVisualSampleEntry>(
                    entry.VisualEntry).Selection);
        });
    }

    [Fact]
    public void Project_RejectsMissingSourceAndStaleEntryBytes()
    {
        AssertResult(Result.MissingDescriptionSelectionProjection, [], null);
        var box = PlaybackIsoBmffAudioSampleEntryParserTests
            .CreateSampleDescription(
                PlaybackIsoBmffAudioSampleEntryParserTests
                    .CreateAudioPayload());
        AssertResult(
            Result.EntryLocationMismatch,
            box.AsSpan()[..^1],
            Source(box, 0x736F756E));
    }

    [Fact]
    public void Project_RejectsInvalidChildLimit()
    {
        var box = PlaybackIsoBmffAudioSampleEntryParserTests
            .CreateSampleDescription(
                PlaybackIsoBmffAudioSampleEntryParserTests
                    .CreateAudioPayload());
        Assert.Equal(
            Result.InvalidConfiguration,
            PlaybackIsoBmffTrackSampleEntryParseProjector.Project(
                box,
                Source(box, 0x736F756E),
                out var projection,
                new() { MaximumChildBoxCount = -1 }));
        Assert.Null(projection);
    }

    internal static PlaybackIsoBmffTrackSampleEntryParseProjection Project(
        byte[] box,
        PlaybackIsoBmffTrackSampleDescriptionSelectionProjection source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffTrackSampleEntryParseProjector.Project(
                box,
                source,
                out var projection));
        return Assert.IsType<PlaybackIsoBmffTrackSampleEntryParseProjection>(
            projection);
    }

    private static void AssertResult(
        Result expected,
        ReadOnlySpan<byte> box,
        PlaybackIsoBmffTrackSampleDescriptionSelectionProjection? source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffTrackSampleEntryParseProjector.Project(
                box,
                source,
                out var projection));
        Assert.Null(projection);
    }

    internal static PlaybackIsoBmffTrackSampleDescriptionSelectionProjection
        Source(byte[] box, uint handlerType)
    {
        Assert.Equal(
            PlaybackIsoBmffSampleDescriptionBoxParseResult.Parsed,
            PlaybackIsoBmffSampleDescriptionBoxParser.Parse(
                box,
                out var sampleDescription));
        return PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionTests
            .Project(
                PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionTests
                    .Candidates(),
                Assert.IsType<PlaybackIsoBmffSampleDescriptionBox>(
                    sampleDescription),
                PlaybackIsoBmffEditedSampleDescriptionSelectorTests
                    .ParseHandler(handlerType));
    }
}
