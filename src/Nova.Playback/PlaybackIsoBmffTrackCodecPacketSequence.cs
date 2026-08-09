using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
{
    Created,
    MissingPackets,
    EmptyPacketSequence,
    PacketLimitExceeded,
    MissingPacket,
    InvalidSampleNumber,
    MovieTimingMismatch,
    TrackSourceMismatch,
    MediaTimingMismatch,
    HandlerMismatch,
    NonIncreasingSampleNumber,
}

public sealed class PlaybackIsoBmffTrackCodecPacketSequenceEntry
{
    internal PlaybackIsoBmffTrackCodecPacketSequenceEntry(
        int sequenceIndex,
        PlaybackIsoBmffMovieTrackCodecPacket packet)
    {
        SequenceIndex = sequenceIndex;
        Packet = packet;
    }

    public int SequenceIndex { get; }

    public PlaybackIsoBmffMovieTrackCodecPacket Packet { get; }

    public ulong SampleNumber =>
        Packet.TrackPacket.Packet.Candidate.SampleNumber;
}

public sealed class PlaybackIsoBmffTrackCodecPacketSequence
{
    internal PlaybackIsoBmffTrackCodecPacketSequence(
        PlaybackIsoBmffMovieHeaderTimingBox movieTiming,
        PlaybackIsoBmffTrackHeaderIdentityBox trackIdentity,
        PlaybackIsoBmffMediaHeaderTimingBox mediaTiming,
        PlaybackIsoBmffHandlerReferenceBox handler,
        ReadOnlyCollection<PlaybackIsoBmffTrackCodecPacketSequenceEntry>
            entries)
    {
        MovieTiming = movieTiming;
        TrackIdentity = trackIdentity;
        MediaTiming = mediaTiming;
        Handler = handler;
        Entries = entries;
    }

    public PlaybackIsoBmffMovieHeaderTimingBox MovieTiming { get; }

    public PlaybackIsoBmffTrackHeaderIdentityBox TrackIdentity { get; }

    public PlaybackIsoBmffMediaHeaderTimingBox MediaTiming { get; }

    public PlaybackIsoBmffHandlerReferenceBox Handler { get; }

    public uint TrackId => TrackIdentity.TrackId;

    public PlaybackIsoBmffHandlerKind MediaKind => Handler.Kind;

    public IReadOnlyList<PlaybackIsoBmffTrackCodecPacketSequenceEntry> Entries
    { get; }
}

public static class PlaybackIsoBmffTrackCodecPacketSequenceFactory
{
    public const int MaximumPacketCount = 131_072;

    public static PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
        TryCreate(
            IReadOnlyList<PlaybackIsoBmffMovieTrackCodecPacket>? packets,
            out PlaybackIsoBmffTrackCodecPacketSequence? sequence)
    {
        sequence = null;
        if (packets is null)
        {
            return PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                .MissingPackets;
        }

        if (packets.Count == 0)
        {
            return PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                .EmptyPacketSequence;
        }

        if (packets.Count > MaximumPacketCount)
        {
            return PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                .PacketLimitExceeded;
        }

        var first = packets[0];
        if (first is null)
        {
            return PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                .MissingPacket;
        }

        var movieTiming = first.MovieTiming;
        var trackIdentity = first.TrackPacket.TrackIdentity;
        var mediaTiming = first.TrackPacket.MediaTiming;
        var handler = first.TrackPacket.Handler;
        var entries = new PlaybackIsoBmffTrackCodecPacketSequenceEntry[
            packets.Count];
        ulong priorSampleNumber = 0;
        for (var index = 0; index < packets.Count; index++)
        {
            var packet = packets[index];
            if (packet is null)
            {
                return PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                    .MissingPacket;
            }

            if (!ReferenceEquals(packet.MovieTiming, movieTiming))
            {
                return PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                    .MovieTimingMismatch;
            }

            if (!ReferenceEquals(
                    packet.TrackPacket.TrackIdentity,
                    trackIdentity))
            {
                return PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                    .TrackSourceMismatch;
            }

            if (!ReferenceEquals(packet.TrackPacket.MediaTiming, mediaTiming))
            {
                return PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                    .MediaTimingMismatch;
            }

            if (!ReferenceEquals(packet.TrackPacket.Handler, handler))
            {
                return PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                    .HandlerMismatch;
            }

            var sampleNumber =
                packet.TrackPacket.Packet.Candidate.SampleNumber;
            if (sampleNumber == 0)
            {
                return PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                    .InvalidSampleNumber;
            }

            if (index > 0 && sampleNumber <= priorSampleNumber)
            {
                return PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                    .NonIncreasingSampleNumber;
            }

            entries[index] = new(index, packet);
            priorSampleNumber = sampleNumber;
        }

        sequence = new(
            movieTiming,
            trackIdentity,
            mediaTiming,
            handler,
            Array.AsReadOnly(entries));
        return PlaybackIsoBmffTrackCodecPacketSequenceCreationResult.Created;
    }
}
