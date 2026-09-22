using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Trak = 0x7472616b;
    private const uint Mdia = 0x6d646961;
    private const uint Hdlr = 0x68646c72;
    private const uint Minf = 0x6d696e66;
    private const uint Stbl = 0x7374626c;
    private const uint Vide = 0x76696465;
    private const int SampleDescriptionOffset = 81;

    [Fact]
    public void Project_Avc1TrackWithConfigurationIsAccepted()
    {
        var (sampleDescriptionBox, _) =
            PlaybackIsoBmffAvcDecoderConfigurationParserTests.CreateVisualEntry(
                PlaybackIsoBmffAvcDecoderConfigurationParserTests
                    .BaselineRecord());
        var segment = CreateInitializationSegment(sampleDescriptionBox);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .Projected,
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var sampleDescriptionProjection));
        var track = Assert.Single(sampleDescriptionProjection!.Tracks);

        var result =
            PlaybackFmp4LiveInitializationAvcTrackConfigurationProjector.Project(
                segment.AsSpan(
                    SampleDescriptionOffset,
                    sampleDescriptionBox.Length),
                track,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                .Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveInitializationAvcTrackConfiguration>(projection);
        Assert.Same(track, projected.Source);
        Assert.Equal(
            PlaybackIsoBmffSampleEntryCodecCandidate.Avc1,
            projected.CodecCandidate);
        Assert.Equal((ushort)640, projected.VisualEntry.Width);
        Assert.Equal((ushort)360, projected.VisualEntry.Height);
        Assert.Equal((byte)66, projected.Configuration.ProfileIndication);
        Assert.Equal((byte)4, projected.Configuration.NalUnitLengthBytes);
        Assert.Collection(
            projected.Configuration.ParameterSets,
            parameterSet => Assert.Equal(
                PlaybackIsoBmffAvcParameterSetKind.Sequence,
                parameterSet.Kind),
            parameterSet => Assert.Equal(
                PlaybackIsoBmffAvcParameterSetKind.Picture,
                parameterSet.Kind));
    }

    [Fact]
    public void Project_Avc1TrackWithoutConfigurationIsRejected()
    {
        var (sampleDescriptionBox, _) =
            PlaybackIsoBmffAvcDecoderConfigurationParserTests.CreateVisualEntry();
        var segment = CreateInitializationSegment(sampleDescriptionBox);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .Projected,
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var sampleDescriptionProjection));
        var track = Assert.Single(sampleDescriptionProjection!.Tracks);

        var result =
            PlaybackFmp4LiveInitializationAvcTrackConfigurationProjector.Project(
                segment.AsSpan(
                    SampleDescriptionOffset,
                    sampleDescriptionBox.Length),
                track,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                .MissingConfigurationBox,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_Avc1TrackWithDuplicateConfigurationIsRejected()
    {
        var record = PlaybackIsoBmffAvcDecoderConfigurationParserTests
            .BaselineRecord();
        var (sampleDescriptionBox, _) =
            PlaybackIsoBmffAvcDecoderConfigurationParserTests.CreateVisualEntry(
                record,
                record);
        var segment = CreateInitializationSegment(sampleDescriptionBox);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .Projected,
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var sampleDescriptionProjection));
        var track = Assert.Single(sampleDescriptionProjection!.Tracks);

        var result =
            PlaybackFmp4LiveInitializationAvcTrackConfigurationProjector.Project(
                segment.AsSpan(
                    SampleDescriptionOffset,
                    sampleDescriptionBox.Length),
                track,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                .DuplicateConfigurationBox,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_Avc1TrackWithMalformedConfigurationIsRejected()
    {
        var record = PlaybackIsoBmffAvcDecoderConfigurationParserTests
            .BaselineRecord();
        record[0] = 2;
        var (sampleDescriptionBox, _) =
            PlaybackIsoBmffAvcDecoderConfigurationParserTests.CreateVisualEntry(
                record);
        var segment = CreateInitializationSegment(sampleDescriptionBox);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .Projected,
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var sampleDescriptionProjection));
        var track = Assert.Single(sampleDescriptionProjection!.Tracks);

        var result =
            PlaybackFmp4LiveInitializationAvcTrackConfigurationProjector.Project(
                segment.AsSpan(
                    SampleDescriptionOffset,
                    sampleDescriptionBox.Length),
                track,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                .InvalidConfigurationPayload,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_Avc1TrackWithoutSequenceParameterSetIsRejected()
    {
        byte[] record =
        [
            1, 66, 0, 30, 0xFF, 0xE0,
            1, 0, 1, 0x68,
        ];
        var (sampleDescriptionBox, _) =
            PlaybackIsoBmffAvcDecoderConfigurationParserTests.CreateVisualEntry(
                record);
        var segment = CreateInitializationSegment(sampleDescriptionBox);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .Projected,
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var sampleDescriptionProjection));
        var track = Assert.Single(sampleDescriptionProjection!.Tracks);

        var result =
            PlaybackFmp4LiveInitializationAvcTrackConfigurationProjector.Project(
                segment.AsSpan(
                    SampleDescriptionOffset,
                    sampleDescriptionBox.Length),
                track,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                .InvalidConfigurationPayload,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_Avc1TrackWithoutPictureParameterSetIsRejected()
    {
        byte[] record =
        [
            1, 66, 0, 30, 0xFF, 0xE1,
            0, 2, 0x67, 0x42,
            0,
        ];
        var (sampleDescriptionBox, _) =
            PlaybackIsoBmffAvcDecoderConfigurationParserTests.CreateVisualEntry(
                record);
        var segment = CreateInitializationSegment(sampleDescriptionBox);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .Projected,
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var sampleDescriptionProjection));
        var track = Assert.Single(sampleDescriptionProjection!.Tracks);

        var result =
            PlaybackFmp4LiveInitializationAvcTrackConfigurationProjector.Project(
                segment.AsSpan(
                    SampleDescriptionOffset,
                    sampleDescriptionBox.Length),
                track,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                .InvalidConfigurationPayload,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_Avc1TrackOverParameterSetCountLimitIsRejected()
    {
        const int sequenceCount = 31;
        const int pictureCount = 226;
        var record = new byte[6 + sequenceCount * 3 + 1 + pictureCount * 3];
        record[0] = 1;
        record[1] = 66;
        record[2] = 0;
        record[3] = 30;
        record[4] = 0xFF;
        record[5] = 0xFF;

        var offset = 6;
        for (var index = 0; index < sequenceCount; index++)
        {
            record[offset++] = 0;
            record[offset++] = 1;
            record[offset++] = 0x67;
        }

        record[offset++] = pictureCount;
        for (var index = 0; index < pictureCount; index++)
        {
            record[offset++] = 0;
            record[offset++] = 1;
            record[offset++] = 0x68;
        }

        Assert.Equal(record.Length, offset);
        var (sampleDescriptionBox, _) =
            PlaybackIsoBmffAvcDecoderConfigurationParserTests.CreateVisualEntry(
                record);
        var segment = CreateInitializationSegment(sampleDescriptionBox);

        Assert.Equal(
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjectionResult
                .Projected,
            PlaybackFmp4LiveInitializationTrackSampleDescriptionProjector.Project(
                segment,
                segment.Length,
                out var sampleDescriptionProjection));
        var track = Assert.Single(sampleDescriptionProjection!.Tracks);

        var result =
            PlaybackFmp4LiveInitializationAvcTrackConfigurationProjector.Project(
                segment.AsSpan(
                    SampleDescriptionOffset,
                    sampleDescriptionBox.Length),
                track,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                .ParameterSetLimitExceeded,
            result);
        Assert.Null(projection);
    }

    private static byte[] CreateInitializationSegment(
        ReadOnlySpan<byte> sampleDescriptionBox)
    {
        var segment = new byte[
            SampleDescriptionOffset + sampleDescriptionBox.Length];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(segment.AsSpan(8, 8), checked((uint)(segment.Length - 8)), Moov);
        WriteHeader(segment.AsSpan(16, 8), checked((uint)(segment.Length - 16)), Trak);
        WriteHeader(segment.AsSpan(24, 8), checked((uint)(segment.Length - 24)), Mdia);
        WriteHandler(segment.AsSpan(32, 33), Vide);
        WriteHeader(segment.AsSpan(65, 8), checked((uint)(segment.Length - 65)), Minf);
        WriteHeader(segment.AsSpan(73, 8), checked((uint)(segment.Length - 73)), Stbl);
        sampleDescriptionBox.CopyTo(segment.AsSpan(SampleDescriptionOffset));
        return segment;
    }

    private static void WriteHandler(Span<byte> destination, uint handlerType)
    {
        WriteHeader(destination[..8], 33, Hdlr);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(16, 4), handlerType);
    }

    private static void WriteHeader(Span<byte> destination, uint size, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], size);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], type);
    }
}
