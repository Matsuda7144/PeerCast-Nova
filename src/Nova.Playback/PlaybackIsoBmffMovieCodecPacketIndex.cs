using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketIndexingResult
{
    Indexed,
    MissingPackets,
    EmptyPacketSet,
    TooManyPackets,
    MissingPacket,
    MovieTimingMismatch,
    DuplicateTrackId,
}

public sealed class PlaybackIsoBmffIndexedMovieTrackCodecPacket
{
    internal PlaybackIsoBmffIndexedMovieTrackCodecPacket(
        int streamIndex,
        PlaybackIsoBmffMovieTrackCodecPacket packet)
    {
        StreamIndex = streamIndex;
        Packet = packet;
    }

    public int StreamIndex { get; }

    public PlaybackIsoBmffMovieTrackCodecPacket Packet { get; }

    public uint TrackId => Packet.TrackId;

    public PlaybackIsoBmffHandlerKind MediaKind =>
        Packet.TrackPacket.MediaKind;
}

public sealed class PlaybackIsoBmffMovieCodecPacketIndex
{
    internal PlaybackIsoBmffMovieCodecPacketIndex(
        PlaybackIsoBmffMovieHeaderTimingBox movieTiming,
        ReadOnlyCollection<PlaybackIsoBmffIndexedMovieTrackCodecPacket>
            streams)
    {
        MovieTiming = movieTiming;
        Streams = streams;
    }

    public PlaybackIsoBmffMovieHeaderTimingBox MovieTiming { get; }

    public IReadOnlyList<PlaybackIsoBmffIndexedMovieTrackCodecPacket> Streams
    {
        get;
    }
}

public static class PlaybackIsoBmffMovieCodecPacketIndexer
{
    public const int MaximumPacketCount = 256;

    public static PlaybackIsoBmffMovieCodecPacketIndexingResult TryIndex(
        IReadOnlyList<PlaybackIsoBmffMovieTrackCodecPacket>? packets,
        out PlaybackIsoBmffMovieCodecPacketIndex? index)
    {
        index = null;
        if (packets is null)
        {
            return PlaybackIsoBmffMovieCodecPacketIndexingResult
                .MissingPackets;
        }

        if (packets.Count == 0)
        {
            return PlaybackIsoBmffMovieCodecPacketIndexingResult
                .EmptyPacketSet;
        }

        if (packets.Count > MaximumPacketCount)
        {
            return PlaybackIsoBmffMovieCodecPacketIndexingResult
                .TooManyPackets;
        }

        var firstPacket = packets[0];
        if (firstPacket is null)
        {
            return PlaybackIsoBmffMovieCodecPacketIndexingResult
                .MissingPacket;
        }

        var movieTiming = firstPacket.MovieTiming;
        var trackIds = new HashSet<uint>();
        var streams = new PlaybackIsoBmffIndexedMovieTrackCodecPacket[
            packets.Count];
        for (var streamIndex = 0;
            streamIndex < packets.Count;
            streamIndex++)
        {
            var packet = packets[streamIndex];
            if (packet is null)
            {
                return PlaybackIsoBmffMovieCodecPacketIndexingResult
                    .MissingPacket;
            }

            if (!ReferenceEquals(packet.MovieTiming, movieTiming))
            {
                return PlaybackIsoBmffMovieCodecPacketIndexingResult
                    .MovieTimingMismatch;
            }

            if (!trackIds.Add(packet.TrackId))
            {
                return PlaybackIsoBmffMovieCodecPacketIndexingResult
                    .DuplicateTrackId;
            }

            streams[streamIndex] = new(streamIndex, packet);
        }

        index = new(movieTiming, Array.AsReadOnly(streams));
        return PlaybackIsoBmffMovieCodecPacketIndexingResult.Indexed;
    }
}
