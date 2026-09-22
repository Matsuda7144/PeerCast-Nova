using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionTests
{
    private const uint Moof = 0x6d6f6f66;
    private const uint Mfhd = 0x6d666864;
    private const uint Traf = 0x74726166;
    private const uint Tfhd = 0x74666864;

    [Theory]
    [InlineData(0x02000000U, PlaybackFmp4LiveMediaFragmentLateJoinEligibilityKind.Eligible, true)]
    [InlineData(0x01000000U, PlaybackFmp4LiveMediaFragmentLateJoinEligibilityKind.NotEligible, false)]
    [InlineData(0x00000000U, PlaybackFmp4LiveMediaFragmentLateJoinEligibilityKind.Indeterminate, false)]
    public void Project_FirstVideoSampleDeterminesEligibility(
        uint sampleFlags,
        PlaybackFmp4LiveMediaFragmentLateJoinEligibilityKind expectedKind,
        bool expectedEligible)
    {
        var identity = Identity(23, 1);
        var videoRun = ResolvedRun(1, sampleFlags);

        var result = PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjector.Project(
            identity,
            1,
            videoRun,
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult.Projected,
            result);
        var projected = Assert.IsType<
            PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjection>(projection);
        Assert.Same(identity, projected.Source);
        Assert.Same(videoRun, projected.VideoRun);
        Assert.Equal((uint)23, projected.SequenceNumber);
        Assert.Equal((uint)1, projected.VideoTrackId);
        Assert.Equal(sampleFlags, projected.FirstVideoSample.SampleFlags);
        Assert.Equal(expectedKind, projected.Kind);
        Assert.Equal(expectedEligible, projected.IsEligible);
    }

    [Fact]
    public void Project_MissingOrUnknownVideoTrackInputsAreRejected()
    {
        var identity = Identity(23, 1);
        var videoRun = ResolvedRun(1, 0x02000000);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult
                .MissingFragmentIdentity,
            PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjector.Project(
                null,
                1,
                videoRun,
                out var missingIdentity));
        Assert.Null(missingIdentity);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult
                .InvalidVideoTrackId,
            PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjector.Project(
                identity,
                0,
                videoRun,
                out var invalidTrackId));
        Assert.Null(invalidTrackId);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult
                .VideoTrackMissing,
            PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjector.Project(
                identity,
                2,
                videoRun,
                out var missingTrack));
        Assert.Null(missingTrack);
    }

    [Fact]
    public void Project_MissingMismatchedOrEmptyVideoRunIsRejected()
    {
        var identity = Identity(23, 1);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult
                .MissingVideoRun,
            PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjector.Project(
                identity,
                1,
                null,
                out var missingRun));
        Assert.Null(missingRun);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult
                .VideoRunTrackIdMismatch,
            PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjector.Project(
                identity,
                1,
                ResolvedRun(2, 0x02000000),
                out var mismatchedRun));
        Assert.Null(mismatchedRun);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult
                .EmptyVideoRun,
            PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjector.Project(
                identity,
                1,
                ResolvedRun(1),
                out var emptyRun));
        Assert.Null(emptyRun);
    }

    [Fact]
    public void Project_ReservedFirstVideoSampleFlagsAreRejected()
    {
        var identity = Identity(23, 1);

        var result = PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjector.Project(
            identity,
            1,
            ResolvedRun(1, 0x03000000),
            out var projection);

        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult
                .InvalidFirstVideoSampleFlags,
            result);
        Assert.Null(projection);
    }

    private static PlaybackFmp4LiveMediaFragmentIdentityProjection Identity(
        uint sequenceNumber,
        params uint[] trackIds)
    {
        var children = new List<byte[]> { MovieFragmentHeader(sequenceNumber) };
        children.AddRange(trackIds.Select(TrackFragment));
        var movieFragment = Box(Moof, children.ToArray());
        Assert.Equal(
            PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected,
            PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
                movieFragment,
                out var identity));
        return Assert.IsType<PlaybackFmp4LiveMediaFragmentIdentityProjection>(
            identity);
    }

    private static PlaybackIsoBmffResolvedFragmentRun ResolvedRun(
        uint trackId,
        params uint[] sampleFlags)
    {
        var trackExtends = ParseTrex(trackId);
        var fragmentHeader = ParseTfhd(trackId);
        var trackRun = ParseTrun(sampleFlags);
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleDefaultsResolveResult.Resolved,
            PlaybackIsoBmffFragmentSampleDefaultsResolver.Resolve(
                trackExtends,
                fragmentHeader,
                trackRun,
                out var resolvedRun));
        return Assert.IsType<PlaybackIsoBmffResolvedFragmentRun>(resolvedRun);
    }

    private static PlaybackIsoBmffTrackExtendsBox ParseTrex(uint trackId)
    {
        var box = FullBox(0x74726578, 0, 20);
        WriteUInt32(box, 12, trackId);
        WriteUInt32(box, 16, 1);
        WriteUInt32(box, 20, 1);
        WriteUInt32(box, 24, 1);
        WriteUInt32(box, 28, 0);
        Assert.Equal(
            PlaybackIsoBmffTrackExtendsBoxParseResult.Parsed,
            PlaybackIsoBmffTrackExtendsBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffTrackExtendsBox>(parsed);
    }

    private static PlaybackIsoBmffTrackFragmentHeaderBox ParseTfhd(uint trackId)
    {
        var box = FullBox(Tfhd, 0, 4);
        WriteUInt32(box, 12, trackId);
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.Parsed,
            PlaybackIsoBmffTrackFragmentHeaderBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffTrackFragmentHeaderBox>(parsed);
    }

    private static PlaybackIsoBmffTrackRunBox ParseTrun(uint[] sampleFlags)
    {
        var flags = sampleFlags.Length == 0 ? 0U : 0x000400U;
        var box = FullBox(
            0x7472756e,
            flags,
            4 + (sampleFlags.Length * 4));
        WriteUInt32(box, 12, checked((uint)sampleFlags.Length));
        for (var index = 0; index < sampleFlags.Length; index++)
        {
            WriteUInt32(box, 16 + (index * 4), sampleFlags[index]);
        }

        Assert.Equal(
            PlaybackIsoBmffTrackRunBoxParseResult.Parsed,
            PlaybackIsoBmffTrackRunBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffTrackRunBox>(parsed);
    }

    private static byte[] MovieFragmentHeader(uint sequenceNumber)
    {
        var box = new byte[16];
        WriteHeader(box, Mfhd);
        WriteUInt32(box, 12, sequenceNumber);
        return box;
    }

    private static byte[] TrackFragment(uint trackId) =>
        Box(Traf, TrackFragmentHeader(trackId));

    private static byte[] TrackFragmentHeader(uint trackId)
    {
        var box = new byte[16];
        WriteHeader(box, Tfhd);
        WriteUInt32(box, 12, trackId);
        return box;
    }

    private static byte[] FullBox(uint type, uint flags, int bodySize)
    {
        var box = new byte[12 + bodySize];
        WriteHeader(box, type);
        box[9] = (byte)(flags >> 16);
        box[10] = (byte)(flags >> 8);
        box[11] = (byte)flags;
        return box;
    }

    private static byte[] Box(uint type, params byte[][] children)
    {
        var box = new byte[8 + children.Sum(child => child.Length)];
        WriteHeader(box, type);
        var offset = 8;
        foreach (var child in children)
        {
            child.CopyTo(box, offset);
            offset += child.Length;
        }

        return box;
    }

    private static void WriteHeader(byte[] box, uint type)
    {
        WriteUInt32(box, 0, checked((uint)box.Length));
        WriteUInt32(box, 4, type);
    }

    private static void WriteUInt32(byte[] box, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(offset, 4), value);
}
