using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Maps serialized, payload-free PCP CHAN control messages into Core status.
/// A live channel session must use one adapter from one consumer path.
/// </summary>
public sealed class PcpChannelStateAdapter
{
    private readonly ChannelStatusTracker _statusTracker;
    private readonly PcpStreamPositionTracker _positionTracker = new();

    public PcpChannelStateAdapter(ChannelStatusTracker statusTracker)
    {
        ArgumentNullException.ThrowIfNull(statusTracker);
        _statusTracker = statusTracker;
    }

    public PcpChannelApplyResult Apply(PcpChannelControlMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var current = _statusTracker.Snapshot;

        if (current.ChannelId.Value != message.ChannelId)
        {
            throw new InvalidDataException(
                "The CHAN message does not belong to this channel session.");
        }

        var metadata = message.Info is null && message.Track is null
            ? null
            : MergeMetadata(current.Metadata, message.Info, message.Track);

        if (message.Packet is null)
        {
            return new PcpChannelApplyResult(
                _statusTracker.ApplyObservation(
                    metadata,
                    streamPosition: null),
                Packet: null,
                Discontinuity: null);
        }

        if (current.State != ChannelSessionState.Receiving)
        {
            throw new InvalidOperationException(
                $"A CHAN packet cannot be applied while the session is {current.State}.");
        }

        var position = _positionTracker.Observe(message.Packet.Position);

        if (position.Kind ==
            PcpStreamPositionObservationKind.BackwardDiscontinuity)
        {
            return new PcpChannelApplyResult(
                _statusTracker.ApplyObservation(
                    metadata,
                    streamPosition: null),
                message.Packet,
                new PcpStreamDiscontinuity(
                    position.PreviousWirePosition!.Value,
                    position.WirePosition,
                    position.AbsolutePosition));
        }

        return new PcpChannelApplyResult(
            _statusTracker.ApplyObservation(
                metadata,
                position.AbsolutePosition),
            message.Packet,
            Discontinuity: null);
    }

    private static ChannelMetadataSnapshot MergeMetadata(
        ChannelMetadataSnapshot current,
        PcpChannelInfo? info,
        PcpChannelTrack? track)
    {
        var nextTrack = current.Track;

        if (track is not null)
        {
            nextTrack = new ChannelTrackMetadataSnapshot(
                track.Title ?? current.Track?.Title,
                track.Genre ?? current.Track?.Genre,
                track.Album ?? current.Track?.Album,
                track.Creator ?? current.Track?.Creator,
                track.Url ?? current.Track?.ContactUrl);
        }

        return new ChannelMetadataSnapshot(
            name: info?.Name ?? current.Name,
            genre: info?.Genre ?? current.Genre,
            description: info?.Description ?? current.Description,
            contactUrl: info?.Url ?? current.ContactUrl,
            contentType: info?.ContentType ?? current.ContentType,
            bitrateKbps: ConvertBitrate(info?.BitrateKbps) ??
                current.BitrateKbps,
            streamType: info?.StreamType ?? current.StreamType,
            comment: info?.Comment ?? current.Comment,
            streamExtension: info?.StreamExtension ??
                current.StreamExtension,
            track: nextTrack);
    }

    private static int? ConvertBitrate(uint? bitrateKbps)
    {
        if (bitrateKbps is null)
        {
            return null;
        }

        if (bitrateKbps > int.MaxValue)
        {
            throw new InvalidDataException(
                "The CHAN bitrate exceeds the Core metadata range.");
        }

        return (int)bitrateKbps.Value;
    }
}
