using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffEditedSamplePacketCandidateTests
{
    [Fact]
    public void TryCreate_BindsOwnedBytesExactTimingAndAppearances()
    {
        var ownedPayload = CreateOwnedPayload();

        var result = PlaybackIsoBmffEditedSamplePacketCandidateFactory.TryCreate(
            ownedPayload,
            ParseMediaTiming(90_000),
            out var candidate);

        Assert.Equal(
            PlaybackIsoBmffEditedSamplePacketCandidateResult.Created,
            result);
        var created = Assert.IsType<
            PlaybackIsoBmffEditedSamplePacketCandidate>(candidate);
        Assert.Same(ownedPayload, created.OwnedPayload);
        Assert.Equal((ulong)1, created.SampleNumber);
        Assert.Equal((uint)90_000, created.MediaTimescale);
        Assert.Equal((Int128)10, created.Presentation.StartTimeUnits);
        Assert.Equal((Int128)12, created.Presentation.EndTimeUnitsExclusive);
        Assert.Single(created.Appearances);
        Assert.Equal(new byte[] { 1, 2 }, created.Payload.ToArray());
    }

    [Fact]
    public void TryCreate_MissingInputsAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffEditedSamplePacketCandidateResult
                .MissingOwnedPayload,
            PlaybackIsoBmffEditedSamplePacketCandidateFactory.TryCreate(
                null,
                ParseMediaTiming(1),
                out var missingPayload));
        Assert.Null(missingPayload);

        Assert.Equal(
            PlaybackIsoBmffEditedSamplePacketCandidateResult.MissingMediaTiming,
            PlaybackIsoBmffEditedSamplePacketCandidateFactory.TryCreate(
                CreateOwnedPayload(),
                null,
                out var missingTiming));
        Assert.Null(missingTiming);
    }

    private static PlaybackIsoBmffOwnedEditedSamplePayload CreateOwnedPayload()
    {
        var sample = new PlaybackIsoBmffSamplePresentationTime(
            10,
            2,
            12,
            0,
            false);
        var appearances = Array.AsReadOnly(
            new[]
            {
                new PlaybackIsoBmffSampleEditMovieOverlap(
                    0,
                    10,
                    12,
                    1,
                    true),
            });
        var admission = new PlaybackIsoBmffEditedSampleAdmission(
            1,
            new PlaybackIsoBmffCompleteSampleEditAdmission(sample, appearances));
        var editedRange = new PlaybackIsoBmffEditedSampleByteRange(
            admission,
            new(8, 2, 10),
            new(0, 1, 1, 0, 1, 1));

        var file = new byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(0, 4), 12);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4, 4), 0x6D646174);
        file[8] = 1;
        file[9] = 2;
        Assert.Equal(
            PlaybackIsoBmffEditedSampleMediaDataLookupResult.Found,
            PlaybackIsoBmffEditedSampleMediaDataLookup.Lookup(
                file,
                editedRange,
                out var mediaData));
        Assert.Equal(
            PlaybackIsoBmffOwnedEditedSamplePayloadResult.Copied,
            PlaybackIsoBmffOwnedEditedSamplePayloadCopy.Copy(
                file,
                mediaData,
                out var ownedPayload));
        return Assert.IsType<PlaybackIsoBmffOwnedEditedSamplePayload>(
            ownedPayload);
    }

    private static PlaybackIsoBmffMediaHeaderTimingBox ParseMediaTiming(
        uint timescale)
    {
        var box = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), 32);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), 0x6D646864);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), timescale);
        BinaryPrimitives.WriteUInt16BigEndian(box.AsSpan(28, 2), 0x55C4);
        Assert.Equal(
            PlaybackIsoBmffMediaHeaderTimingBoxParseResult.Parsed,
            PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(box, out var timing));
        return Assert.IsType<PlaybackIsoBmffMediaHeaderTimingBox>(timing);
    }
}
