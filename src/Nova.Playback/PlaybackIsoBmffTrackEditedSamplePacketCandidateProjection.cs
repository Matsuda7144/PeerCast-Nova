using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackEditedSamplePacketCandidateProjectionResult
{
    Projected,
    MissingOwnedPayloadProjection,
    MissingMediaTiming,
    EntryLimitExceeded,
    SourceMismatch,
    MediaTimingMismatch,
    InvalidSampleTiming,
    MissingEditAppearance,
}

public sealed class PlaybackIsoBmffTrackEditedSamplePacketCandidate
{
    internal PlaybackIsoBmffTrackEditedSamplePacketCandidate(
        PlaybackIsoBmffTrackOwnedSamplePayload source,
        PlaybackIsoBmffEditedSamplePacketCandidate candidate)
    {
        Source = source;
        Candidate = candidate;
    }

    public PlaybackIsoBmffTrackOwnedSamplePayload Source { get; }

    public PlaybackIsoBmffEditedSamplePacketCandidate Candidate { get; }

    public ulong SampleNumber => Source.SampleNumber;

    public ReadOnlyMemory<byte> Payload => Candidate.Payload;
}

public sealed class PlaybackIsoBmffTrackEditedSamplePacketCandidateProjection
{
    internal PlaybackIsoBmffTrackEditedSamplePacketCandidateProjection(
        PlaybackIsoBmffTrackOwnedSamplePayloadProjection source,
        PlaybackIsoBmffMediaHeaderTimingBox mediaTiming,
        ReadOnlyCollection<PlaybackIsoBmffTrackEditedSamplePacketCandidate>
            entries)
    {
        Source = source;
        MediaTiming = mediaTiming;
        Entries = entries;
    }

    public PlaybackIsoBmffTrackOwnedSamplePayloadProjection Source { get; }

    public PlaybackIsoBmffMediaHeaderTimingBox MediaTiming { get; }

    public IReadOnlyList<PlaybackIsoBmffTrackEditedSamplePacketCandidate>
        Entries
    { get; }
}

public static class PlaybackIsoBmffTrackEditedSamplePacketCandidateProjector
{
    public static
        PlaybackIsoBmffTrackEditedSamplePacketCandidateProjectionResult Project(
            PlaybackIsoBmffTrackOwnedSamplePayloadProjection? source,
            PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming,
            out PlaybackIsoBmffTrackEditedSamplePacketCandidateProjection?
                projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffTrackEditedSamplePacketCandidateProjectionResult
                    .MissingOwnedPayloadProjection;
        }

        if (mediaTiming is null)
        {
            return
                PlaybackIsoBmffTrackEditedSamplePacketCandidateProjectionResult
                    .MissingMediaTiming;
        }

        if (source.Entries.Count >
            PlaybackIsoBmffTrackSampleSequencePlanFactory
                .MaximumPlannedSampleCount)
        {
            return
                PlaybackIsoBmffTrackEditedSamplePacketCandidateProjectionResult
                    .EntryLimitExceeded;
        }

        var admissionProjection = source.Source.Source.Source.Source;
        if (!ReferenceEquals(mediaTiming, admissionProjection.MediaTiming))
        {
            return
                PlaybackIsoBmffTrackEditedSamplePacketCandidateProjectionResult
                    .MediaTimingMismatch;
        }

        var entries = new PlaybackIsoBmffTrackEditedSamplePacketCandidate[
            source.Entries.Count];
        for (var index = 0; index < source.Entries.Count; index++)
        {
            var entry = source.Entries[index];
            if (!ReferenceEquals(entry.Source, source.Source.Entries[index]) ||
                !ReferenceEquals(
                    entry.OwnedPayload.MediaData,
                    entry.Source.MediaData))
            {
                return
                    PlaybackIsoBmffTrackEditedSamplePacketCandidateProjectionResult
                        .SourceMismatch;
            }

            var result = PlaybackIsoBmffEditedSamplePacketCandidateFactory
                .TryCreate(
                    entry.OwnedPayload,
                    mediaTiming,
                    out var candidate);
            if (result != PlaybackIsoBmffEditedSamplePacketCandidateResult.Created ||
                candidate is null)
            {
                return result ==
                    PlaybackIsoBmffEditedSamplePacketCandidateResult
                        .MissingEditAppearance
                    ? PlaybackIsoBmffTrackEditedSamplePacketCandidateProjectionResult
                        .MissingEditAppearance
                    : PlaybackIsoBmffTrackEditedSamplePacketCandidateProjectionResult
                        .InvalidSampleTiming;
            }

            entries[index] = new(entry, candidate);
        }

        projection = new(source, mediaTiming, Array.AsReadOnly(entries));
        return PlaybackIsoBmffTrackEditedSamplePacketCandidateProjectionResult
            .Projected;
    }
}
