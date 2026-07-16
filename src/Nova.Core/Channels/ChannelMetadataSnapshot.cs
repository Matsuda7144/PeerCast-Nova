namespace PeerCast.Nova.Core.Channels;

/// <summary>
/// Immutable, user-visible channel metadata after protocol-specific decoding
/// and resource-limit checks have completed.
/// </summary>
public sealed record ChannelMetadataSnapshot
{
    public ChannelMetadataSnapshot(
        string? name = null,
        string? genre = null,
        string? description = null,
        string? contactUrl = null,
        string? contentType = null,
        int? bitrateKbps = null,
        string? streamType = null,
        string? comment = null,
        string? streamExtension = null,
        ChannelTrackMetadataSnapshot? track = null)
    {
        if (bitrateKbps is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitrateKbps),
                bitrateKbps,
                "Channel bitrate cannot be negative.");
        }

        Name = name;
        Genre = genre;
        Description = description;
        ContactUrl = contactUrl;
        ContentType = contentType;
        BitrateKbps = bitrateKbps;
        StreamType = streamType;
        Comment = comment;
        StreamExtension = streamExtension;
        Track = track;
    }

    public static ChannelMetadataSnapshot Empty { get; } = new();

    public string? Name { get; }

    public string? Genre { get; }

    public string? Description { get; }

    public string? ContactUrl { get; }

    public string? ContentType { get; }

    public int? BitrateKbps { get; }

    public string? StreamType { get; }

    public string? Comment { get; }

    public string? StreamExtension { get; }

    public ChannelTrackMetadataSnapshot? Track { get; }
}
