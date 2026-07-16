namespace PeerCast.Nova.Protocol.Pcp;

public sealed record PcpChannelControlMessage(
    Guid ChannelId,
    PcpChannelPacketHeader? Packet,
    PcpChannelInfo? Info,
    PcpChannelTrack? Track);

/// <summary>
/// Describes packet control fields without retaining the media payload.
/// </summary>
public sealed record PcpChannelPacketHeader(
    uint Type,
    uint Position,
    int DataLength);

public sealed record PcpChannelInfo(
    string? ContentType,
    string? StreamType,
    string? Name,
    string? Genre,
    string? Description,
    string? Comment,
    string? Url,
    string? StreamExtension,
    uint? BitrateKbps);

public sealed record PcpChannelTrack(
    string? Title,
    string? Genre,
    string? Album,
    string? Creator,
    string? Url);
