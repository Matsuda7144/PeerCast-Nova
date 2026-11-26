using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

internal sealed record GenesisRecordingCandidate(
    DailyPcpPlaybackSource Source,
    RecordingCandidate Candidate);

/// <summary>
/// Converts only selected-row data that is present and agrees with the
/// canonical PCP source into Recording Core's candidate contract.
/// </summary>
internal static class GenesisRecordingCandidateAdapter
{
    public static bool TryCreate(
        GenesisChannelItem? channel,
        out GenesisRecordingCandidate? candidate)
    {
        candidate = null;
        if (channel is null ||
            string.IsNullOrWhiteSpace(channel.Name) ||
            channel.Name.Length > GenesisChannelCatalogSanitizer.MaximumNameCharacters ||
            channel.Description?.Length > GenesisChannelCatalogSanitizer.MaximumDescriptionCharacters ||
            channel.Type?.Length > GenesisChannelCatalogSanitizer.MaximumTypeCharacters ||
            channel.BitrateKbps is < 0 ||
            !DailyPcpPlaybackSourceParser.TryParse(channel.Source, out var source) ||
            source is null ||
            !TryGetChannelId(channel, out var rowChannelId) ||
            rowChannelId != source.ChannelId)
        {
            return false;
        }

        var metadata = new ChannelMetadataSnapshot(
            name: channel.Name.Trim(),
            description: channel.Description,
            bitrateKbps: channel.BitrateKbps,
            streamType: channel.Type,
            streamExtension: GetRecordingStreamExtension(channel.Type));
        candidate = new(source, new RecordingCandidate(rowChannelId, metadata));
        return true;
    }

    private static string? GetRecordingStreamExtension(string? streamType) =>
        string.Equals(
            streamType?.Trim(),
            "FLV",
            StringComparison.OrdinalIgnoreCase)
                ? "flv"
                : null;

    public static bool TryGetChannelId(
        GenesisChannelItem? channel,
        out ChannelId? channelId)
    {
        channelId = null;
        if (channel is null ||
            string.IsNullOrWhiteSpace(channel.ChannelId) ||
            !Guid.TryParse(channel.ChannelId, out var value) ||
            value == Guid.Empty)
        {
            return false;
        }

        channelId = new ChannelId(value);
        return true;
    }
}
