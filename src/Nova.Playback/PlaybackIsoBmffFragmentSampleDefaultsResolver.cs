using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleDefaultsResolveResult
{
    Resolved,
    InvalidInput,
    TrackIdMismatch,
    InvalidSampleDescriptionIndex,
    SampleCountMismatch,
}

public readonly record struct PlaybackIsoBmffResolvedFragmentSample(
    uint SampleDuration,
    uint SampleSize,
    uint SampleFlags,
    long? SampleCompositionTimeOffset);

public sealed class PlaybackIsoBmffResolvedFragmentRun
{
    internal PlaybackIsoBmffResolvedFragmentRun(
        uint trackId,
        uint sampleDescriptionIndex,
        ReadOnlyCollection<PlaybackIsoBmffResolvedFragmentSample> samples)
    {
        TrackId = trackId;
        SampleDescriptionIndex = sampleDescriptionIndex;
        Samples = samples;
    }

    public uint TrackId { get; }

    public uint SampleDescriptionIndex { get; }

    public IReadOnlyList<PlaybackIsoBmffResolvedFragmentSample> Samples { get; }
}

public static class PlaybackIsoBmffFragmentSampleDefaultsResolver
{
    public static PlaybackIsoBmffFragmentSampleDefaultsResolveResult Resolve(
        PlaybackIsoBmffTrackExtendsBox? trackExtends,
        PlaybackIsoBmffTrackFragmentHeaderBox? fragmentHeader,
        PlaybackIsoBmffTrackRunBox? trackRun,
        out PlaybackIsoBmffResolvedFragmentRun? resolvedRun)
    {
        resolvedRun = null;
        if (trackExtends is null || fragmentHeader is null || trackRun is null)
        {
            return PlaybackIsoBmffFragmentSampleDefaultsResolveResult.InvalidInput;
        }

        if (trackExtends.TrackId != fragmentHeader.TrackId)
        {
            return
                PlaybackIsoBmffFragmentSampleDefaultsResolveResult
                    .TrackIdMismatch;
        }

        var sampleDescriptionIndex =
            fragmentHeader.SampleDescriptionIndex ??
            trackExtends.DefaultSampleDescriptionIndex;
        if (sampleDescriptionIndex == 0)
        {
            return
                PlaybackIsoBmffFragmentSampleDefaultsResolveResult
                    .InvalidSampleDescriptionIndex;
        }

        if ((uint)trackRun.Entries.Count != trackRun.SampleCount)
        {
            return
                PlaybackIsoBmffFragmentSampleDefaultsResolveResult
                    .SampleCountMismatch;
        }

        var defaultDuration =
            fragmentHeader.DefaultSampleDuration ??
            trackExtends.DefaultSampleDuration;
        var defaultSize =
            fragmentHeader.DefaultSampleSize ??
            trackExtends.DefaultSampleSize;
        var defaultFlags =
            fragmentHeader.DefaultSampleFlags ??
            trackExtends.DefaultSampleFlags;
        var samples = new PlaybackIsoBmffResolvedFragmentSample[
            trackRun.Entries.Count];
        for (var index = 0; index < samples.Length; index++)
        {
            var entry = trackRun.Entries[index];
            var sampleFlags = entry.SampleFlags ??
                (index == 0 ? trackRun.FirstSampleFlags : null) ??
                defaultFlags;
            samples[index] = new(
                entry.SampleDuration ?? defaultDuration,
                entry.SampleSize ?? defaultSize,
                sampleFlags,
                entry.SampleCompositionTimeOffset);
        }

        resolvedRun = new(
            fragmentHeader.TrackId,
            sampleDescriptionIndex,
            Array.AsReadOnly(samples));
        return PlaybackIsoBmffFragmentSampleDefaultsResolveResult.Resolved;
    }
}
