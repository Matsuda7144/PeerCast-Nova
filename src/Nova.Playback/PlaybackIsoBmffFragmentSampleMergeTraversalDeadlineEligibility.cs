using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityPolicy
{
    ReadyOnly,
    ReadyOrExpired,
}

public enum PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityKind
{
    IneligiblePending,
    EligibleReady,
    IneligibleExpired,
    EligibleExpired,
}

public enum PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityResult
{
    Projected,
    InvalidInput,
    InvalidPolicy,
    SourceMismatch,
}

public sealed record
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityOptions
{
    public PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityPolicy
        DecodePolicy
    { get; init; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityPolicy
        PresentationPolicy
    { get; init; }
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibility
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibility(
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservation observation,
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityKind
            decodeKind,
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityKind
            presentationKind)
    {
        Observation = observation;
        DecodeKind = decodeKind;
        PresentationKind = presentationKind;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservation
        Observation
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityKind
        DecodeKind
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityKind
        PresentationKind
    { get; }

    public bool IsDecodeEligible => IsEligible(DecodeKind);

    public bool IsPresentationEligible => IsEligible(PresentationKind);

    public bool AreBothEligible =>
        IsDecodeEligible && IsPresentationEligible;

    private static bool IsEligible(
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityKind kind) =>
        kind is
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityKind
                .EligibleReady or
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityKind
                .EligibleExpired;
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSnapshot
                source,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityOptions
                options,
            ReadOnlyCollection<
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibility>
                entries)
    {
        Source = source;
        Options = options;
        Entries = entries;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSnapshot
            Source
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityOptions
        Options
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibility> Entries
    { get; }

    public int SampleCount => Entries.Count;
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineObservationSnapshot?
                    source,
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityOptions?
                    options,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityProjection?
                        projection)
    {
        projection = null;
        if (source is null || options is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityResult
                    .InvalidInput;
        }

        if (!Enum.IsDefined(options.DecodePolicy) ||
            !Enum.IsDefined(options.PresentationPolicy))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityResult
                    .InvalidPolicy;
        }

        if (source.SampleCount != source.Source.SampleCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityResult
                    .SourceMismatch;
        }

        var entries = new
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibility[
                source.SampleCount];
        for (var index = 0; index < entries.Length; index++)
        {
            var observation = source.Observations[index];
            if (!ReferenceEquals(
                    observation.Deadline,
                    source.Source.Deadlines[index]))
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityResult
                        .SourceMismatch;
            }

            entries[index] = new(
                observation,
                Classify(observation.DecodeState, options.DecodePolicy),
                Classify(
                    observation.PresentationState,
                    options.PresentationPolicy));
        }

        projection = new(source, options, Array.AsReadOnly(entries));
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityResult
                .Projected;
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityKind
            Classify(
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineState state,
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityPolicy
                    policy) =>
        state switch
        {
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineState.Pending =>
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityKind
                    .IneligiblePending,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineState.Ready =>
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityKind
                    .EligibleReady,
            PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineState.Expired
                when policy ==
                    PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityPolicy
                        .ReadyOrExpired =>
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityKind
                    .EligibleExpired,
            _ =>
                PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityKind
                    .IneligibleExpired,
        };
}
