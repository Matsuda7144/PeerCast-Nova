namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffAvcSampleFramingTests
{
    [Fact]
    public void Frame_ProjectsExactNalUnitRangesWithoutPayloadCopies()
    {
        var candidate = CreateCandidate(
            [0, 0, 0, 2, 0x65, 0xAA, 0, 0, 0, 1, 0x06]);
        var configuration = CreateConfiguration(4);

        var result = PlaybackIsoBmffAvcSampleFraming.Frame(
            candidate,
            configuration,
            out var frame);

        Assert.Equal(PlaybackIsoBmffAvcSampleFramingResult.Framed, result);
        var framed = Assert.IsType<PlaybackIsoBmffAvcSampleFrame>(frame);
        Assert.Same(candidate, framed.Candidate);
        Assert.Same(configuration, framed.DecoderConfiguration);
        Assert.Equal((byte)4, framed.NalUnitLengthBytes);
        Assert.Equal(3, framed.NalUnitPayloadBytes);
        Assert.Collection(
            framed.NalUnits,
            first =>
            {
                Assert.Equal(4, first.PayloadOffset);
                Assert.Equal(2, first.Length);
                Assert.Equal(new byte[] { 0x65, 0xAA },
                    first.Payload.ToArray());
            },
            second =>
            {
                Assert.Equal(10, second.PayloadOffset);
                Assert.Equal(1, second.Length);
                Assert.Equal(new byte[] { 0x06 }, second.Payload.ToArray());
            });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Frame_AcceptsEveryConfiguredNalLengthWidth(int lengthBytes)
    {
        var payload = new byte[lengthBytes + 2];
        payload[lengthBytes - 1] = 2;
        payload[^2] = 0x67;
        payload[^1] = 0x42;

        Assert.Equal(
            PlaybackIsoBmffAvcSampleFramingResult.Framed,
            PlaybackIsoBmffAvcSampleFraming.Frame(
                CreateCandidate(payload),
                CreateConfiguration((byte)lengthBytes),
                out var frame));
        var unit = Assert.Single(frame!.NalUnits);
        Assert.Equal(lengthBytes, unit.PayloadOffset);
        Assert.Equal(new byte[] { 0x67, 0x42 }, unit.Payload.ToArray());
    }

    [Fact]
    public void Frame_MissingInputsAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffAvcSampleFramingResult.MissingCandidate,
            PlaybackIsoBmffAvcSampleFraming.Frame(
                null,
                CreateConfiguration(4),
                out var missingCandidate));
        Assert.Null(missingCandidate);

        Assert.Equal(
            PlaybackIsoBmffAvcSampleFramingResult
                .MissingDecoderConfiguration,
            PlaybackIsoBmffAvcSampleFraming.Frame(
                CreateCandidate([1, 0x65]),
                null,
                out var missingConfiguration));
        Assert.Null(missingConfiguration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65_537)]
    public void Frame_InvalidLimitRejectsBeforeInputAccess(int maximumCount)
    {
        Assert.Equal(
            PlaybackIsoBmffAvcSampleFramingResult.InvalidConfiguration,
            PlaybackIsoBmffAvcSampleFraming.Frame(
                null,
                null,
                out var frame,
                new() { MaximumNalUnitCount = maximumCount }));
        Assert.Null(frame);
    }

    [Fact]
    public void Frame_RequiresMatchingSampleDescription()
    {
        Assert.Equal(
            PlaybackIsoBmffAvcSampleFramingResult.SampleDescriptionMismatch,
            PlaybackIsoBmffAvcSampleFraming.Frame(
                CreateCandidate([1, 0x65], sampleDescriptionIndex: 2),
                CreateConfiguration(1),
                out var frame));
        Assert.Null(frame);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Frame_RejectsUnsupportedNalLengthWidth(int lengthBytes)
    {
        Assert.Equal(
            PlaybackIsoBmffAvcSampleFramingResult
                .UnsupportedDecoderConfiguration,
            PlaybackIsoBmffAvcSampleFraming.Frame(
                CreateCandidate([1, 0x65]),
                CreateConfiguration((byte)lengthBytes),
                out var frame));
        Assert.Null(frame);
    }

    [Fact]
    public void Frame_RejectsNonAvcSelection()
    {
        Assert.Equal(
            PlaybackIsoBmffAvcSampleFramingResult
                .UnsupportedDecoderConfiguration,
            PlaybackIsoBmffAvcSampleFraming.Frame(
                CreateCandidate([1, 0x65]),
                CreateConfiguration(
                    1,
                    PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a,
                    PlaybackIsoBmffHandlerKind.Audio),
                out var frame));
        Assert.Null(frame);
    }

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public void Frame_MalformedLengthPrefixedPayloadFailsClosed(byte[] payload)
    {
        Assert.Equal(
            PlaybackIsoBmffAvcSampleFramingResult.InvalidPayload,
            PlaybackIsoBmffAvcSampleFraming.Frame(
                CreateCandidate(payload),
                CreateConfiguration(2),
                out var frame));
        Assert.Null(frame);
    }

    [Fact]
    public void Frame_NalUnitCountLimitIsEnforced()
    {
        Assert.Equal(
            PlaybackIsoBmffAvcSampleFramingResult.NalUnitLimitExceeded,
            PlaybackIsoBmffAvcSampleFraming.Frame(
                CreateCandidate([1, 0x65, 1, 0x06]),
                CreateConfiguration(1),
                out var frame,
                new() { MaximumNalUnitCount = 1 }));
        Assert.Null(frame);
    }

    public static TheoryData<byte[]> InvalidPayloads => new()
    {
        Array.Empty<byte>(),
        new byte[] { 0 },
        new byte[] { 0, 0 },
        new byte[] { 0, 2, 0x65 },
        new byte[] { 0, 1, 0x65, 0 },
    };

    private static PlaybackIsoBmffEditedSamplePacketCandidate CreateCandidate(
        byte[] payload,
        uint sampleDescriptionIndex = 1)
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
            new(8, checked((uint)payload.Length),
                checked((ulong)payload.Length + 8)),
            new(0, 1, 1, 0, 1, sampleDescriptionIndex));
        var mediaData = new PlaybackIsoBmffEditedSampleMediaData(
            editedRange,
            Location(0x6D646174));
        var owned = new PlaybackIsoBmffOwnedEditedSamplePayload(
            mediaData,
            payload);
        return new(owned, 90_000);
    }

    private static PlaybackIsoBmffAvcDecoderConfiguration CreateConfiguration(
        byte lengthBytes,
        PlaybackIsoBmffSampleEntryCodecCandidate codec =
            PlaybackIsoBmffSampleEntryCodecCandidate.Avc1,
        PlaybackIsoBmffHandlerKind mediaKind = PlaybackIsoBmffHandlerKind.Video)
    {
        var selection = new PlaybackIsoBmffEditedSampleDescriptionSelection(
            1,
            Location(codec == PlaybackIsoBmffSampleEntryCodecCandidate.Avc3
                ? 0x61766333u
                : 0x61766331u),
            codec,
            mediaKind);
        var visual = new PlaybackIsoBmffVisualSampleEntry(
            selection,
            1,
            640,
            360,
            0,
            0,
            1,
            [],
            0x18,
            Array.AsReadOnly(Array.Empty<PlaybackIsoBmffBoxLocation>()));
        return new(
            visual,
            Location(0x61766343),
            66,
            0,
            30,
            lengthBytes,
            null,
            null,
            null,
            Array.AsReadOnly(Array.Empty<PlaybackIsoBmffAvcParameterSet>()),
            [1]);
    }

    private static PlaybackIsoBmffBoxLocation Location(uint type) =>
        new(new(type, 8, 8, null), 0, 8);
}
