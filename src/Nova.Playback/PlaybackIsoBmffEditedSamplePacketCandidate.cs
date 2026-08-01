namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffEditedSamplePacketCandidateResult
{
    Created,
    MissingOwnedPayload,
    MissingMediaTiming,
    InvalidSampleTiming,
    MissingEditAppearance,
}

public sealed class PlaybackIsoBmffEditedSamplePacketCandidate
{
    internal PlaybackIsoBmffEditedSamplePacketCandidate(
        PlaybackIsoBmffOwnedEditedSamplePayload ownedPayload,
        uint mediaTimescale)
    {
        OwnedPayload = ownedPayload;
        MediaTimescale = mediaTimescale;
    }

    public PlaybackIsoBmffOwnedEditedSamplePayload OwnedPayload { get; }

    public ulong SampleNumber => OwnedPayload.SampleNumber;

    public ReadOnlyMemory<byte> Payload => OwnedPayload.Payload;

    public PlaybackIsoBmffSamplePresentationTime Presentation =>
        OwnedPayload.MediaData.Admission.Sample;

    public IReadOnlyList<PlaybackIsoBmffSampleEditMovieOverlap> Appearances =>
        OwnedPayload.MediaData.Admission.Appearances;

    public uint MediaTimescale { get; }
}

public static class PlaybackIsoBmffEditedSamplePacketCandidateFactory
{
    public static PlaybackIsoBmffEditedSamplePacketCandidateResult TryCreate(
        PlaybackIsoBmffOwnedEditedSamplePayload? ownedPayload,
        PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming,
        out PlaybackIsoBmffEditedSamplePacketCandidate? candidate)
    {
        candidate = null;
        if (ownedPayload is null)
        {
            return PlaybackIsoBmffEditedSamplePacketCandidateResult
                .MissingOwnedPayload;
        }

        if (mediaTiming is null)
        {
            return PlaybackIsoBmffEditedSamplePacketCandidateResult
                .MissingMediaTiming;
        }

        var presentation = ownedPayload.MediaData.Admission.Sample;
        if (presentation.EndTimeUnitsExclusive !=
            presentation.StartTimeUnits + presentation.DurationUnits)
        {
            return PlaybackIsoBmffEditedSamplePacketCandidateResult
                .InvalidSampleTiming;
        }

        if (ownedPayload.MediaData.Admission.Appearances.Count == 0)
        {
            return PlaybackIsoBmffEditedSamplePacketCandidateResult
                .MissingEditAppearance;
        }

        candidate = new(ownedPayload, mediaTiming.Timescale);
        return PlaybackIsoBmffEditedSamplePacketCandidateResult.Created;
    }
}
