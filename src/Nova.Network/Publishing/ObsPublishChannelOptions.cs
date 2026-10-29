using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Publishing;

public sealed record ObsPublishChannelOptions
{
    public ObsRtmpIngestOptions Ingest { get; init; } = new();
    public PcpHttpFlvPlaybackOptions Http { get; init; } = new();
    public PcpRelayFanoutOptions Fanout { get; init; } = new();
    public int RelayPort { get; init; }
    public ObsPublishLanOptions Lan { get; init; } = new();
    public PublishingAnnounceOptions Announce { get; init; } = new();
    public ObsPublishWanOptions Wan { get; init; } = new();
    public PublishingRelayMode RelayMode => Wan.Enabled ? PublishingRelayMode.Wan : Lan.Enabled ? PublishingRelayMode.Lan : PublishingRelayMode.Loopback;
    public string ChannelName { get; init; } = "Local publish";
    public string Genre { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Comment { get; init; } = string.Empty;
    public string ContactUrl { get; init; } = string.Empty;
    public int? BitrateKbps { get; init; }
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public PeerCast.Nova.Core.Channels.ChannelMetadataSnapshot CreateMetadata() => ObsPublishChannelMetadata.Create(this);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Ingest);
        ArgumentNullException.ThrowIfNull(Http);
        ArgumentNullException.ThrowIfNull(Fanout);
        ArgumentNullException.ThrowIfNull(Lan);
        Lan.Validate();
        ArgumentNullException.ThrowIfNull(Wan);
        Wan.Validate();
        Announce.Validate();
        if (Lan.Enabled && Wan.Enabled) throw new ObsPublishWanException(ObsPublishWanFailure.InvalidSettings);
        _ = CreateMetadata();
        Ingest.Validate();
        Http.Validate();
        Fanout.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(RelayPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(RelayPort, 65535);
        if (DrainTimeout <= TimeSpan.Zero ||
            DrainTimeout > TimeSpan.FromMinutes(1) ||
            Fanout.MaxPendingPacketsPerChild < 2 ||
            Fanout.MaxPendingBytesPerChild < 2L * Ingest.MaximumPacketBytes)
        {
            throw new ArgumentException("Publish limits cannot support the mandatory bounded HTTP relay child.");
        }
    }
}

public enum ObsPublishChannelEndReason
{
    PublisherStopped,
    Disconnected,
    ProtocolRejected,
    ConsumerFaulted,
    HostCancelled,
    TimedOut,
    TransportFailed,
    StartupFailed,
    DownstreamFailed,
    InternalFailure,
    LocalTerminated,
}

/// <summary>Accepted callbacks and bounded drain evidence, not physical playback.</summary>
public sealed record ObsPublishChannelResult(
    ObsPublishChannelEndReason EndReason,
    long AcceptedPacketCount,
    long AcceptedByteCount,
    long HeadCount,
    long DataCount,
    PcpRelayDrainOutcome RelayDrain,
    bool HttpDrained,
    bool ShutdownCompleted,
    ObsRtmpIngestResult? Ingest,
    PcpDownstreamRelayServerResult? Relay)
{
    public ObsRtmpRejectionDiagnostic? Rejection { get; init; }
    public ObsPublishLocalTerminationResult? LocalTermination { get; init; }
}
