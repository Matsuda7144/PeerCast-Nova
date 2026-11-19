using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjectionResult
{
    Projected,
    MissingTargetBindingProjection,
    BindingLimitExceeded,
    SourceMismatch,
    TargetMismatch,
    MediaMismatch,
    EmptyPayload,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequest
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequest(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBinding
                binding)
    {
        Binding = binding;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBinding
            Binding
    { get; }

    public Guid TargetId => Binding.Target.TargetId;

    public PlaybackIsoBmffHandlerKind MediaKind =>
        Binding.Candidate.Event.Packet.TrackPacket.MediaKind;

    public PlaybackIsoBmffCodecPacketProjection Packet =>
        Binding.Candidate.Event.Packet.TrackPacket.Packet;

    public ReadOnlyMemory<byte> Payload => Packet.Payload;

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDeadline
            Deadline => Binding.Candidate.Eligibility.Observation.Deadline;
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingProjection
                source,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequest>
                requests)
    {
        Source = source;
        Requests = requests;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingProjection
            Source
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequest>
        Requests
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjectionResult
            TryProject(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingProjection?
                    source,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjection?
                    projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjectionResult
                    .MissingTargetBindingProjection;
        }

        if (source.Bindings.Count >
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjectionResult
                    .BindingLimitExceeded;
        }

        if (source.Bindings.Count != source.Selection.Candidates.Count)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjectionResult
                    .SourceMismatch;
        }

        var requests = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequest[
                source.Bindings.Count];
        for (var index = 0; index < source.Bindings.Count; index++)
        {
            var binding = source.Bindings[index];
            if (!ReferenceEquals(
                    binding.Candidate,
                    source.Selection.Candidates[index]))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjectionResult
                        .SourceMismatch;
            }

            var mediaKind = binding.Candidate.Event.Packet.TrackPacket.MediaKind;
            var expectedTarget = mediaKind switch
            {
                PlaybackIsoBmffHandlerKind.Video => source.TargetSet.VideoTarget,
                PlaybackIsoBmffHandlerKind.Audio => source.TargetSet.AudioTarget,
                _ => null,
            };
            if (!ReferenceEquals(binding.Target, expectedTarget))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjectionResult
                        .TargetMismatch;
            }

            if (binding.Target.MediaKind != mediaKind)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjectionResult
                        .MediaMismatch;
            }

            if (binding.Candidate.Event.Packet.TrackPacket.Packet.Payload.IsEmpty)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjectionResult
                        .EmptyPayload;
            }

            requests[index] = new(binding);
        }

        projection = new(source, Array.AsReadOnly(requests));
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjectionResult
                .Projected;
    }
}
