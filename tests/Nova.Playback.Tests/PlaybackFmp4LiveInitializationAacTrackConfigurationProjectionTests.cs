using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Trak = 0x7472616b;
    private const uint Mdia = 0x6d646961;
    private const uint Hdlr = 0x68646c72;
    private const uint Minf = 0x6d696e66;
    private const uint Stbl = 0x7374626c;
    private const uint Soun = 0x736f756e;
    private const int SampleDescriptionOffset = 81;

    [Fact]
    public void Project_Mp4aTrackWithConfigurationIsAccepted()
    {
        var elementaryStream =
            PlaybackIsoBmffElementaryStreamDescriptorParserTests
                .BaselineElementaryStream();
        var (sampleDescriptionBox, _) =
            PlaybackIsoBmffElementaryStreamDescriptorParserTests.CreateAudioEntry(
                PlaybackIsoBmffElementaryStreamDescriptorParserTests
                    .ElementaryStreamBox(elementaryStream));
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
            PlaybackFmp4LiveInitializationAacTrackConfigurationProjector.Project(
                segment.AsSpan(
                    SampleDescriptionOffset,
                    sampleDescriptionBox.Length),
                track,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
                .Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveInitializationAacTrackConfiguration>(projection);
        Assert.Same(track, projected.Source);
        Assert.Equal(
            PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a,
            projected.CodecCandidate);
        Assert.Equal((ushort)2, projected.AudioEntry.ChannelCount);
        Assert.Equal((ushort)48_000, projected.AudioEntry.IntegerSampleRate);
        Assert.Equal(
            (ushort)0x1234,
            projected.ElementaryStreamDescriptor.ElementaryStreamId);
        Assert.Collection(
            projected.ElementaryStreamDescriptor.Children,
            decoder => Assert.Equal((byte)0x04, decoder.Header.Tag),
            sl => Assert.Equal((byte)0x06, sl.Header.Tag));
    }

    [Fact]
    public void Project_Mp4aTrackWithoutConfigurationIsRejected()
    {
        var (sampleDescriptionBox, _) =
            PlaybackIsoBmffElementaryStreamDescriptorParserTests.CreateAudioEntry();
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
            PlaybackFmp4LiveInitializationAacTrackConfigurationProjector.Project(
                segment.AsSpan(
                    SampleDescriptionOffset,
                    sampleDescriptionBox.Length),
                track,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
                .MissingConfigurationBox,
            result);
        Assert.Null(projection);
    }

    [Fact]
    public void Project_Mp4aTrackWithDuplicateConfigurationIsRejected()
    {
        var elementaryStream =
            PlaybackIsoBmffElementaryStreamDescriptorParserTests
                .BaselineElementaryStream();
        var configuration =
            PlaybackIsoBmffElementaryStreamDescriptorParserTests
                .ElementaryStreamBox(elementaryStream);
        var (sampleDescriptionBox, _) =
            PlaybackIsoBmffElementaryStreamDescriptorParserTests.CreateAudioEntry(
                configuration,
                configuration);
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
            PlaybackFmp4LiveInitializationAacTrackConfigurationProjector.Project(
                segment.AsSpan(
                    SampleDescriptionOffset,
                    sampleDescriptionBox.Length),
                track,
                out var projection);

        Assert.Equal(
            PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
                .DuplicateConfigurationBox,
            result);
        Assert.Null(projection);
    }

    private static byte[] CreateInitializationSegment(
        ReadOnlySpan<byte> sampleDescriptionBox)
    {
        var segment = new byte[
            SampleDescriptionOffset + sampleDescriptionBox.Length];
        WriteHeader(segment.AsSpan(0, 8), 8, Ftyp);
        WriteHeader(
            segment.AsSpan(8, 8),
            checked((uint)(segment.Length - 8)),
            Moov);
        WriteHeader(
            segment.AsSpan(16, 8),
            checked((uint)(segment.Length - 16)),
            Trak);
        WriteHeader(
            segment.AsSpan(24, 8),
            checked((uint)(segment.Length - 24)),
            Mdia);
        WriteHandler(segment.AsSpan(32, 33), Soun);
        WriteHeader(
            segment.AsSpan(65, 8),
            checked((uint)(segment.Length - 65)),
            Minf);
        WriteHeader(
            segment.AsSpan(73, 8),
            checked((uint)(segment.Length - 73)),
            Stbl);
        sampleDescriptionBox.CopyTo(segment.AsSpan(SampleDescriptionOffset));
        return segment;
    }

    private static void WriteHandler(Span<byte> destination, uint handlerType)
    {
        WriteHeader(destination[..8], 33, Hdlr);
        BinaryPrimitives.WriteUInt32BigEndian(
            destination.Slice(16, 4),
            handlerType);
    }

    private static void WriteHeader(Span<byte> destination, uint size, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], size);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], type);
    }
}
