using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
{
    Denied,
    Admitted,
}

public enum
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
{
    Projected,
    MissingInvocationIntentProjection,
    MissingDisposition,
    IntentLimitExceeded,
    DispositionCountMismatch,
    InvalidDisposition,
    SourceMismatch,
    InvalidInvocationIdentity,
    InvalidRuntimeIdentity,
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmission
{
    internal
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmission(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntent
                intent,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
                disposition)
    {
        Intent = intent;
        Disposition = disposition;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntent
        Intent
    { get; }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
        Disposition
    { get; }

    public bool IsAdmitted => Disposition ==
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition
            .Admitted;
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionProjection
{
    internal
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionProjection(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentProjection
                source,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmission>
                admissions)
    {
        Source = source;
        Admissions = admissions;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentProjection
        Source
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmission>
        Admissions
    { get; }
}

public static class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionProjector
{
    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
        Project(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentProjection?
                source,
            IReadOnlyList<
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionDisposition>?
                dispositions,
            out PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionProjection?
                projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                    .MissingInvocationIntentProjection;
        }

        if (dispositions is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                    .MissingDisposition;
        }

        if (source.Intents.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                    .IntentLimitExceeded;
        }

        if (dispositions.Count != source.Intents.Count)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                    .DispositionCountMismatch;
        }

        if (source.Intents.Count != source.Source.Lookups.Count)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                    .SourceMismatch;
        }

        if (source.InvocationId == Guid.Empty)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                    .InvalidInvocationIdentity;
        }

        var admissions = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmission[
                source.Intents.Count];
        for (var index = 0; index < source.Intents.Count; index++)
        {
            var intent = source.Intents[index];
            if (!ReferenceEquals(intent.Lookup, source.Source.Lookups[index]) ||
                intent.SequenceIndex != index)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                        .SourceMismatch;
            }

            if (intent.InvocationId != source.InvocationId)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                        .InvalidInvocationIdentity;
            }

            if (intent.RuntimeInstanceId == Guid.Empty ||
                intent.RuntimeGeneration == 0)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                        .InvalidRuntimeIdentity;
            }

            var disposition = dispositions[index];
            if (!Enum.IsDefined(disposition))
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                        .InvalidDisposition;
            }

            admissions[index] = new(intent, disposition);
        }

        projection = new(source, Array.AsReadOnly(admissions));
        return
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationAdmissionResult
                .Projected;
    }
}
