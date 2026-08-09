using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionResult
{
    Projected,
    MissingTargetBindingProjection,
    BindingLimitExceeded,
    SourceMismatch,
    TargetMismatch,
    MediaMismatch,
    EmptyPayload,
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequest
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequest(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBinding binding)
    {
        Binding = binding;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBinding Binding
    {
        get;
    }

    public Guid TargetId => Binding.Target.TargetId;

    public PlaybackIsoBmffHandlerKind MediaKind =>
        Binding.Candidate.Entry.Event.MediaKind;

    public PlaybackIsoBmffCodecPacketProjection Packet =>
        Binding.Candidate.Entry.Event.Packet;

    public ReadOnlyMemory<byte> Payload => Packet.Payload;

    public PlaybackIsoBmffMovieCodecPacketEventQueueDeadline Deadline =>
        Binding.Candidate.Eligibility.Observation.Deadline;
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjection
{
    internal
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjection(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingProjection
                source,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequest>
                requests)
    {
        Source = source;
        Requests = requests;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingProjection
        Source
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequest> Requests
    {
        get;
    }
}

public static class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjector
{
    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionResult
        Project(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingProjection?
                source,
            out PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjection?
                projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionResult
                    .MissingTargetBindingProjection;
        }

        if (source.Bindings.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionResult
                    .BindingLimitExceeded;
        }

        if (source.Bindings.Count != source.Selection.Candidates.Count)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionResult
                    .SourceMismatch;
        }

        var requests = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequest[
                source.Bindings.Count];
        for (var index = 0; index < source.Bindings.Count; index++)
        {
            var binding = source.Bindings[index];
            if (!ReferenceEquals(
                    binding.Candidate,
                    source.Selection.Candidates[index]))
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionResult
                        .SourceMismatch;
            }

            var expectedTarget = binding.Candidate.Entry.Event.MediaKind switch
            {
                PlaybackIsoBmffHandlerKind.Video => source.TargetSet.VideoTarget,
                PlaybackIsoBmffHandlerKind.Audio => source.TargetSet.AudioTarget,
                _ => null,
            };
            if (!ReferenceEquals(binding.Target, expectedTarget))
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionResult
                        .TargetMismatch;
            }

            if (binding.Target.MediaKind !=
                binding.Candidate.Entry.Event.MediaKind)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionResult
                        .MediaMismatch;
            }

            if (binding.Candidate.Entry.Event.Packet.Payload.IsEmpty)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionResult
                        .EmptyPayload;
            }

            requests[index] = new(binding);
        }

        projection = new(source, Array.AsReadOnly(requests));
        return
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjectionResult
                .Projected;
    }
}
