using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Maps serialized, payload-free PCP CHAN control messages into Core status.
/// A live channel session must use one adapter from one consumer path.
/// </summary>
public sealed class PcpChannelStateAdapter
{
    private const uint HeadPacketType = 0x64616568;
    private const uint DataPacketType = 0x61746164;

    private readonly ChannelStatusTracker _statusTracker;
    private PcpStreamPositionTracker _positionTracker = new();
    private bool _dataBaselinePending;
    private long? _coreStreamPositionFloor;
    private int _declaredBitrateKbps;

    internal int? DeclaredBitrateKbps => Volatile.Read(ref _declaredBitrateKbps) is > 0 and var value ? value : null;
    internal void BeginAttempt() => Volatile.Write(ref _declaredBitrateKbps, 0);

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
                ApplyObservation(message,
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

        var isFirstDataAfterHead =
            message.Packet.Type == DataPacketType &&
            _dataBaselinePending;

        if (message.Packet.Type == HeadPacketType)
        {
            _dataBaselinePending = true;
            _coreStreamPositionFloor = null;
        }
        else if (isFirstDataAfterHead)
        {
            // A relay may send a retained HEAD followed by current DATA whose
            // position is far from the retained header. The first DATA starts
            // the continuity domain used for subsequent DATA packets.
            _positionTracker = new PcpStreamPositionTracker();
            _dataBaselinePending = false;
        }

        var position = _positionTracker.Observe(message.Packet.Position);
        var streamPosition = position.AbsolutePosition;
        if (message.Packet.Type == DataPacketType)
        {
            if (isFirstDataAfterHead &&
                current.StreamPosition is long currentStreamPosition &&
                streamPosition < currentStreamPosition)
            {
                // Core stream position is monotonic. A lower first DATA
                // position is still a valid new DATA baseline, but must not
                // move the Core snapshot backwards without an explicit
                // discontinuity.
                _coreStreamPositionFloor = currentStreamPosition;
                streamPosition = currentStreamPosition;
            }
            else if (_coreStreamPositionFloor is long floor)
            {
                if (streamPosition < floor)
                {
                    streamPosition = floor;
                }
                else
                {
                    _coreStreamPositionFloor = null;
                }
            }
        }

        if (position.Kind ==
            PcpStreamPositionObservationKind.BackwardDiscontinuity)
        {
            return new PcpChannelApplyResult(
                ApplyObservation(message,
                    metadata,
                    streamPosition: null),
                message.Packet,
                new PcpStreamDiscontinuity(
                    position.PreviousWirePosition!.Value,
                    position.WirePosition,
                    position.AbsolutePosition));
        }

        return new PcpChannelApplyResult(
            ApplyObservation(message,
                metadata,
                streamPosition),
            message.Packet,
            Discontinuity: null);
    }

    private ChannelStatusSnapshot ApplyObservation(PcpChannelControlMessage message,
        ChannelMetadataSnapshot? metadata, long? streamPosition)
    {
        var snapshot = _statusTracker.ApplyObservation(metadata, streamPosition);
        if (message.Info?.BitrateKbps is { } bitrate)
            Volatile.Write(ref _declaredBitrateKbps, checked((int)bitrate));
        return snapshot;
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
