using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentRunDataOriginResolveResult
{
    Resolved,
    InvalidInput,
    NegativeCallerPosition,
    MissingImplicitBaseDataOffset,
    MissingImplicitRunDataStart,
    NegativeResolvedRunDataStart,
}

public enum PlaybackIsoBmffFragmentBaseDataOriginKind
{
    TrackFragmentExplicit,
    MovieFragment,
    CallerImplicit,
}

public enum PlaybackIsoBmffFragmentRunDataStartKind
{
    TrackRunRelativeOffset,
    CallerImplicit,
}

public sealed record PlaybackIsoBmffFragmentRunDataOriginContext(
    BigInteger MovieFragmentOffset,
    BigInteger? ImplicitBaseDataOffset = null,
    BigInteger? ImplicitRunDataStart = null);

public sealed class PlaybackIsoBmffFragmentRunDataOrigin
{
    internal PlaybackIsoBmffFragmentRunDataOrigin(
        BigInteger baseDataOffset,
        PlaybackIsoBmffFragmentBaseDataOriginKind baseDataOriginKind,
        BigInteger runDataStart,
        PlaybackIsoBmffFragmentRunDataStartKind runDataStartKind,
        int? relativeDataOffset)
    {
        BaseDataOffset = baseDataOffset;
        BaseDataOriginKind = baseDataOriginKind;
        RunDataStart = runDataStart;
        RunDataStartKind = runDataStartKind;
        RelativeDataOffset = relativeDataOffset;
    }

    public BigInteger BaseDataOffset { get; }

    public PlaybackIsoBmffFragmentBaseDataOriginKind BaseDataOriginKind { get; }

    public BigInteger RunDataStart { get; }

    public PlaybackIsoBmffFragmentRunDataStartKind RunDataStartKind { get; }

    public int? RelativeDataOffset { get; }
}

public static class PlaybackIsoBmffFragmentRunDataOriginResolver
{
    public static PlaybackIsoBmffFragmentRunDataOriginResolveResult Resolve(
        PlaybackIsoBmffTrackFragmentHeaderBox? fragmentHeader,
        PlaybackIsoBmffTrackRunBox? trackRun,
        PlaybackIsoBmffFragmentRunDataOriginContext? context,
        out PlaybackIsoBmffFragmentRunDataOrigin? dataOrigin)
    {
        dataOrigin = null;
        if (fragmentHeader is null || trackRun is null || context is null)
        {
            return PlaybackIsoBmffFragmentRunDataOriginResolveResult.InvalidInput;
        }

        if (context.MovieFragmentOffset < BigInteger.Zero ||
            context.ImplicitBaseDataOffset < BigInteger.Zero ||
            context.ImplicitRunDataStart < BigInteger.Zero)
        {
            return
                PlaybackIsoBmffFragmentRunDataOriginResolveResult
                    .NegativeCallerPosition;
        }

        BigInteger baseDataOffset;
        PlaybackIsoBmffFragmentBaseDataOriginKind baseDataOriginKind;
        if (fragmentHeader.BaseDataOffset is { } explicitBase)
        {
            baseDataOffset = explicitBase;
            baseDataOriginKind =
                PlaybackIsoBmffFragmentBaseDataOriginKind.TrackFragmentExplicit;
        }
        else if (fragmentHeader.DefaultBaseIsMoof)
        {
            baseDataOffset = context.MovieFragmentOffset;
            baseDataOriginKind =
                PlaybackIsoBmffFragmentBaseDataOriginKind.MovieFragment;
        }
        else if (context.ImplicitBaseDataOffset is { } implicitBase)
        {
            baseDataOffset = implicitBase;
            baseDataOriginKind =
                PlaybackIsoBmffFragmentBaseDataOriginKind.CallerImplicit;
        }
        else
        {
            return
                PlaybackIsoBmffFragmentRunDataOriginResolveResult
                    .MissingImplicitBaseDataOffset;
        }

        BigInteger runDataStart;
        PlaybackIsoBmffFragmentRunDataStartKind runDataStartKind;
        if (trackRun.DataOffset is { } relativeOffset)
        {
            runDataStart = baseDataOffset + relativeOffset;
            if (runDataStart < BigInteger.Zero)
            {
                return
                    PlaybackIsoBmffFragmentRunDataOriginResolveResult
                        .NegativeResolvedRunDataStart;
            }

            runDataStartKind =
                PlaybackIsoBmffFragmentRunDataStartKind.TrackRunRelativeOffset;
        }
        else if (context.ImplicitRunDataStart is { } implicitRunStart)
        {
            runDataStart = implicitRunStart;
            runDataStartKind =
                PlaybackIsoBmffFragmentRunDataStartKind.CallerImplicit;
        }
        else
        {
            return
                PlaybackIsoBmffFragmentRunDataOriginResolveResult
                    .MissingImplicitRunDataStart;
        }

        dataOrigin = new(
            baseDataOffset,
            baseDataOriginKind,
            runDataStart,
            runDataStartKind,
            trackRun.DataOffset);
        return PlaybackIsoBmffFragmentRunDataOriginResolveResult.Resolved;
    }
}
