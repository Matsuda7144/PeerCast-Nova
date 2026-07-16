namespace PeerCast.Nova.Core.Channels;

/// <summary>
/// Immutable, user-visible metadata for the item currently carried by a
/// channel. Values are already protocol-decoded and resource-limited.
/// </summary>
public sealed record ChannelTrackMetadataSnapshot(
    string? Title = null,
    string? Genre = null,
    string? Album = null,
    string? Creator = null,
    string? ContactUrl = null);
