using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveMediaFragmentProfileBindingTests
{
    private const uint Ftyp = 0x66747970;
    private const uint Moov = 0x6d6f6f76;
    private const uint Trak = 0x7472616b;
    private const uint Tkhd = 0x746b6864;
    private const uint Mdia = 0x6d646961;
    private const uint Hdlr = 0x68646c72;
    private const uint Minf = 0x6d696e66;
    private const uint Stbl = 0x7374626c;
    private const uint Vide = 0x76696465;
    private const uint Soun = 0x736f756e;
    private const uint Moof = 0x6d6f6f66;
    private const uint Mfhd = 0x6d666864;
    private const uint Traf = 0x74726166;
    private const uint Tfhd = 0x74666864;
    private const uint Tfdt = 0x74666474;
    private const int TkhdBoxLength = 92;

    [Fact]
    public void Bind_KnownTracksAreBoundByTrackIdRegardlessOfFragmentOrder()
    {
        var profile = ProjectInitializationProfile();
        var (identity, timeline) = ProjectFragment(
            33,
            (9U, 48_000UL),
            (7U, 90_000UL));

        var result = PlaybackFmp4LiveMediaFragmentProfileBinder.Bind(
            profile,
            identity,
            timeline,
            out var binding);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentProfileBindingResult.Bound,
            result);
        var bound = Assert.IsType<PlaybackFmp4LiveMediaFragmentProfileBinding>(
            binding);
        Assert.Same(profile, bound.InitializationProfile);
        Assert.Same(identity, bound.FragmentIdentity);
        Assert.Same(timeline, bound.DecodeTimeline);
        Assert.Equal(33U, bound.SequenceNumber);
        Assert.True(bound.ContainsVideo);
        Assert.True(bound.ContainsAudio);
        Assert.Equal(7U, bound.VideoTrack!.TrackId);
        Assert.Equal(90_000UL, bound.VideoTrack.BaseMediaDecodeTime);
        Assert.Equal(9U, bound.AudioTrack!.TrackId);
        Assert.Equal(48_000UL, bound.AudioTrack.BaseMediaDecodeTime);
    }

    [Fact]
    public void Bind_VideoOnlyFragmentIsBoundWithoutInventingAudio()
    {
        var profile = ProjectInitializationProfile();
        var (identity, timeline) = ProjectFragment(34, (7U, 90_000UL));

        var result = PlaybackFmp4LiveMediaFragmentProfileBinder.Bind(
            profile,
            identity,
            timeline,
            out var binding);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentProfileBindingResult.Bound,
            result);
        var bound = Assert.IsType<PlaybackFmp4LiveMediaFragmentProfileBinding>(
            binding);
        Assert.True(bound.ContainsVideo);
        Assert.False(bound.ContainsAudio);
        Assert.Equal(7U, bound.VideoTrack!.TrackId);
        Assert.Null(bound.AudioTrack);
    }

    [Fact]
    public void Bind_AudioOnlyFragmentIsBoundWithoutInventingVideo()
    {
        var profile = ProjectInitializationProfile();
        var (identity, timeline) = ProjectFragment(35, (9U, 48_000UL));

        var result = PlaybackFmp4LiveMediaFragmentProfileBinder.Bind(
            profile,
            identity,
            timeline,
            out var binding);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentProfileBindingResult.Bound,
            result);
        var bound = Assert.IsType<PlaybackFmp4LiveMediaFragmentProfileBinding>(
            binding);
        Assert.False(bound.ContainsVideo);
        Assert.True(bound.ContainsAudio);
        Assert.Null(bound.VideoTrack);
        Assert.Equal(9U, bound.AudioTrack!.TrackId);
    }

    [Fact]
    public void Bind_UnknownTrackIdFailsClosed()
    {
        var profile = ProjectInitializationProfile();
        var (identity, timeline) = ProjectFragment(36, (11U, 1_000UL));

        var result = PlaybackFmp4LiveMediaFragmentProfileBinder.Bind(
            profile,
            identity,
            timeline,
            out var binding);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentProfileBindingResult.UnknownTrackId,
            result);
        Assert.Null(binding);
    }

    [Fact]
    public void Bind_DecodeTimelineFromDifferentIdentityFailsClosed()
    {
        var profile = ProjectInitializationProfile();
        var (identity, _) = ProjectFragment(37, (7U, 90_000UL));
        var (_, differentTimeline) = ProjectFragment(38, (7U, 90_000UL));

        var result = PlaybackFmp4LiveMediaFragmentProfileBinder.Bind(
            profile,
            identity,
            differentTimeline,
            out var binding);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentProfileBindingResult
                .DecodeTimelineIdentityMismatch,
            result);
        Assert.Null(binding);
    }

    [Fact]
    public void Bind_MissingInputsFailClosed()
    {
        var profile = ProjectInitializationProfile();
        var (identity, timeline) = ProjectFragment(39, (7U, 90_000UL));

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentProfileBindingResult
                .MissingInitializationProfile,
            PlaybackFmp4LiveMediaFragmentProfileBinder.Bind(
                null,
                identity,
                timeline,
                out var missingProfile));
        Assert.Null(missingProfile);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentProfileBindingResult
                .MissingFragmentIdentity,
            PlaybackFmp4LiveMediaFragmentProfileBinder.Bind(
                profile,
                null,
                timeline,
                out var missingIdentity));
        Assert.Null(missingIdentity);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentProfileBindingResult
                .MissingDecodeTimeline,
            PlaybackFmp4LiveMediaFragmentProfileBinder.Bind(
                profile,
                identity,
                null,
                out var missingTimeline));
        Assert.Null(missingTimeline);
    }

    private static PlaybackFmp4LiveInitializationProfileProjection
        ProjectInitializationProfile()
    {
        var segment = Concat(
            Box(Ftyp),
            Box(
                Moov,
                InitializationTrack(7, Vide, ValidVideoSampleDescription()),
                InitializationTrack(9, Soun, ValidAudioSampleDescription())));

        Assert.Equal(
            PlaybackFmp4LiveInitializationProfileProjectionResult.Projected,
            PlaybackFmp4LiveInitializationProfileProjector.Project(
                segment,
                segment.Length,
                out var profile));
        return Assert.IsType<PlaybackFmp4LiveInitializationProfileProjection>(
            profile);
    }

    private static (
        PlaybackFmp4LiveMediaFragmentIdentityProjection Identity,
        PlaybackFmp4LiveMediaFragmentDecodeTimelineProjection Timeline)
        ProjectFragment(
            uint sequenceNumber,
            params (uint TrackId, ulong DecodeTime)[] tracks)
    {
        var children = new byte[tracks.Length + 1][];
        children[0] = MovieFragmentHeader(sequenceNumber);
        for (var index = 0; index < tracks.Length; index++)
        {
            children[index + 1] = TrackFragment(
                tracks[index].TrackId,
                TrackDecodeTime(tracks[index].DecodeTime));
        }

        var fragment = Box(Moof, children);
        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
                fragment,
                out var identity));
        var projectedIdentity = Assert.IsType<
            PlaybackFmp4LiveMediaFragmentIdentityProjection>(identity);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentDecodeTimelineProjector.Project(
                fragment,
                projectedIdentity,
                out var timeline));
        return (
            projectedIdentity,
            Assert.IsType<PlaybackFmp4LiveMediaFragmentDecodeTimelineProjection>(
                timeline));
    }

    private static byte[] ValidVideoSampleDescription()
    {
        var (box, _) = PlaybackIsoBmffAvcDecoderConfigurationParserTests
            .CreateVisualEntry(
                PlaybackIsoBmffAvcDecoderConfigurationParserTests
                    .BaselineRecord());
        return box;
    }

    private static byte[] ValidAudioSampleDescription()
    {
        var (box, _) = PlaybackIsoBmffElementaryStreamDescriptorParserTests
            .CreateAudioEntry(
                PlaybackIsoBmffElementaryStreamDescriptorParserTests
                    .ElementaryStreamBox(
                        PlaybackIsoBmffElementaryStreamDescriptorParserTests
                            .BaselineElementaryStream()));
        return box;
    }

    private static byte[] InitializationTrack(
        uint trackId,
        uint handlerType,
        byte[] sampleDescription) =>
        Box(
            Trak,
            TrackHeader(trackId),
            Box(
                Mdia,
                Handler(handlerType),
                Box(Minf, Box(Stbl, sampleDescription))));

    private static byte[] TrackHeader(uint trackId)
    {
        var box = new byte[TkhdBoxLength];
        WriteHeader(box, Tkhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), trackId);
        return box;
    }

    private static byte[] Handler(uint handlerType)
    {
        var box = new byte[33];
        WriteHeader(box, Hdlr);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(16, 4), handlerType);
        return box;
    }

    private static byte[] MovieFragmentHeader(uint sequenceNumber)
    {
        var box = new byte[16];
        WriteHeader(box, Mfhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), sequenceNumber);
        return box;
    }

    private static byte[] TrackFragment(uint trackId, params byte[][] children)
    {
        var allChildren = new byte[children.Length + 1][];
        allChildren[0] = TrackFragmentHeader(trackId);
        children.CopyTo(allChildren, 1);
        return Box(Traf, allChildren);
    }

    private static byte[] TrackFragmentHeader(uint trackId)
    {
        var box = new byte[16];
        WriteHeader(box, Tfhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4), trackId);
        return box;
    }

    private static byte[] TrackDecodeTime(ulong decodeTime)
    {
        var box = new byte[16];
        WriteHeader(box, Tfdt);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            checked((uint)decodeTime));
        return box;
    }

    private static byte[] Box(uint type, params byte[][] payloads)
    {
        var box = new byte[8 + payloads.Sum(payload => payload.Length)];
        WriteHeader(box, type);
        var offset = 8;
        foreach (var payload in payloads)
        {
            payload.CopyTo(box, offset);
            offset += payload.Length;
        }

        return box;
    }

    private static byte[] Concat(params byte[][] values)
    {
        var result = new byte[values.Sum(value => value.Length)];
        var offset = 0;
        foreach (var value in values)
        {
            value.CopyTo(result, offset);
            offset += value.Length;
        }

        return result;
    }

    private static void WriteHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            checked((uint)box.Length));
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
