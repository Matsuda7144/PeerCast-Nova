using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentRunDataOriginResolverTests
{
    [Fact]
    public void Resolve_ExplicitTrackFragmentBaseAndSignedRunOffsetWin()
    {
        var result = PlaybackIsoBmffFragmentRunDataOriginResolver.Resolve(
            FragmentHeader(baseDataOffset: 1_000, defaultBaseIsMoof: true),
            TrackRun(dataOffset: -25),
            new(MovieFragmentOffset: 500),
            out var dataOrigin);

        Assert.Equal(
            PlaybackIsoBmffFragmentRunDataOriginResolveResult.Resolved,
            result);
        var resolved = Assert.IsType<PlaybackIsoBmffFragmentRunDataOrigin>(
            dataOrigin);
        Assert.Equal(new BigInteger(1_000), resolved.BaseDataOffset);
        Assert.Equal(
            PlaybackIsoBmffFragmentBaseDataOriginKind.TrackFragmentExplicit,
            resolved.BaseDataOriginKind);
        Assert.Equal(new BigInteger(975), resolved.RunDataStart);
        Assert.Equal(
            PlaybackIsoBmffFragmentRunDataStartKind.TrackRunRelativeOffset,
            resolved.RunDataStartKind);
        Assert.Equal(-25, resolved.RelativeDataOffset);
    }

    [Fact]
    public void Resolve_DefaultBaseIsMovieFragmentUsesCallerMovieFragmentOffset()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentRunDataOriginResolveResult.Resolved,
            PlaybackIsoBmffFragmentRunDataOriginResolver.Resolve(
                FragmentHeader(defaultBaseIsMoof: true),
                TrackRun(dataOffset: 64),
                new(MovieFragmentOffset: BigInteger.Pow(2, 100)),
                out var dataOrigin));
        var resolved = Assert.IsType<PlaybackIsoBmffFragmentRunDataOrigin>(
            dataOrigin);
        Assert.Equal(
            BigInteger.Pow(2, 100),
            resolved.BaseDataOffset);
        Assert.Equal(
            BigInteger.Pow(2, 100) + 64,
            resolved.RunDataStart);
        Assert.Equal(
            PlaybackIsoBmffFragmentBaseDataOriginKind.MovieFragment,
            resolved.BaseDataOriginKind);
    }

    [Fact]
    public void Resolve_CallerImplicitPositionsRemainExplicitlyClassified()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentRunDataOriginResolveResult.Resolved,
            PlaybackIsoBmffFragmentRunDataOriginResolver.Resolve(
                FragmentHeader(),
                TrackRun(),
                new(
                    MovieFragmentOffset: 100,
                    ImplicitBaseDataOffset: 120,
                    ImplicitRunDataStart: 140),
                out var dataOrigin));
        var resolved = Assert.IsType<PlaybackIsoBmffFragmentRunDataOrigin>(
            dataOrigin);
        Assert.Equal(new BigInteger(120), resolved.BaseDataOffset);
        Assert.Equal(new BigInteger(140), resolved.RunDataStart);
        Assert.Equal(
            PlaybackIsoBmffFragmentBaseDataOriginKind.CallerImplicit,
            resolved.BaseDataOriginKind);
        Assert.Equal(
            PlaybackIsoBmffFragmentRunDataStartKind.CallerImplicit,
            resolved.RunDataStartKind);
        Assert.Null(resolved.RelativeDataOffset);
    }

    [Fact]
    public void Resolve_MissingImplicitBaseIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentRunDataOriginResolveResult
                .MissingImplicitBaseDataOffset,
            PlaybackIsoBmffFragmentRunDataOriginResolver.Resolve(
                FragmentHeader(),
                TrackRun(dataOffset: 0),
                new(MovieFragmentOffset: 0),
                out var dataOrigin));
        Assert.Null(dataOrigin);
    }

    [Fact]
    public void Resolve_MissingImplicitRunStartIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentRunDataOriginResolveResult
                .MissingImplicitRunDataStart,
            PlaybackIsoBmffFragmentRunDataOriginResolver.Resolve(
                FragmentHeader(defaultBaseIsMoof: true),
                TrackRun(),
                new(MovieFragmentOffset: 0),
                out var dataOrigin));
        Assert.Null(dataOrigin);
    }

    [Fact]
    public void Resolve_NegativeCallerPositionsAreRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentRunDataOriginResolveResult
                .NegativeCallerPosition,
            PlaybackIsoBmffFragmentRunDataOriginResolver.Resolve(
                FragmentHeader(defaultBaseIsMoof: true),
                TrackRun(),
                new(
                    MovieFragmentOffset: 0,
                    ImplicitRunDataStart: -1),
                out var dataOrigin));
        Assert.Null(dataOrigin);
    }

    [Fact]
    public void Resolve_NegativeRelativeResultIsRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentRunDataOriginResolveResult
                .NegativeResolvedRunDataStart,
            PlaybackIsoBmffFragmentRunDataOriginResolver.Resolve(
                FragmentHeader(baseDataOffset: 10),
                TrackRun(dataOffset: -11),
                new(MovieFragmentOffset: 0),
                out var dataOrigin));
        Assert.Null(dataOrigin);
    }

    [Fact]
    public void Resolve_NullInputsAreRejectedWithoutProjection()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentRunDataOriginResolveResult.InvalidInput,
            PlaybackIsoBmffFragmentRunDataOriginResolver.Resolve(
                null,
                null,
                null,
                out var dataOrigin));
        Assert.Null(dataOrigin);
    }

    private static PlaybackIsoBmffTrackFragmentHeaderBox FragmentHeader(
        ulong? baseDataOffset = null,
        bool defaultBaseIsMoof = false) =>
        new(
            trackId: 1,
            baseDataOffset,
            sampleDescriptionIndex: null,
            defaultSampleDuration: null,
            defaultSampleSize: null,
            defaultSampleFlags: null,
            durationIsEmpty: false,
            defaultBaseIsMoof);

    private static PlaybackIsoBmffTrackRunBox TrackRun(int? dataOffset = null) =>
        new(
            version: 0,
            sampleCount: 0,
            dataOffset,
            firstSampleFlags: null,
            new ReadOnlyCollection<PlaybackIsoBmffTrackRunEntry>([]));
}
