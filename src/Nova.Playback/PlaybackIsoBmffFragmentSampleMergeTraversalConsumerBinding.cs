using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingResult
{
    Bound,
    MissingRequestSourceProjection,
    MissingConsumerIdentity,
    RequestLimitExceeded,
    SourceMismatch,
    TrackMismatch,
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBinding
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBinding(
        PlaybackIsoBmffFragmentSampleMergeTraversalRequest request,
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentity consumer)
    {
        Request = request;
        Consumer = consumer;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalRequest Request { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentity Consumer
    { get; }
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjection
                source,
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentity
                consumer,
            ReadOnlyCollection<
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBinding>
                bindings)
    {
        Source = source;
        Consumer = consumer;
        Bindings = bindings;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjection
        Source
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentity Consumer
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBinding> Bindings
    { get; }

    public int BindingCount => Bindings.Count;
}

public static class PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBinder
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingResult Bind(
            PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjection?
                source,
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentity?
                consumer,
            out
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingProjection?
                    projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingResult
                    .MissingRequestSourceProjection;
        }

        if (consumer is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingResult
                    .MissingConsumerIdentity;
        }

        var requests = source.Requests;
        if (requests.RequestCount >
            PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingResult
                    .RequestLimitExceeded;
        }

        if (!ReferenceEquals(requests.Source, source.Source.Selection))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingResult
                    .SourceMismatch;
        }

        var bindings =
            new PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBinding[
                requests.RequestCount];
        for (var index = 0; index < bindings.Length; index++)
        {
            var request = requests.Requests[index];
            if (request.RequestIndex != index ||
                !ReferenceEquals(
                    request.Candidate,
                    requests.Source.Candidates[index]))
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingResult
                        .SourceMismatch;
            }

            if (request.TrackId == 0 || request.TrackId != requests.TrackId)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingResult
                        .TrackMismatch;
            }

            bindings[index] = new(request, consumer);
        }

        projection = new(source, consumer, Array.AsReadOnly(bindings));
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingResult
                .Bound;
    }
}
