using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Publishing;

public sealed record ObsPublishChannelOptions
{
    public ObsRtmpIngestOptions Ingest { get; init; } = new();
    public PcpHttpFlvPlaybackOptions Http { get; init; } = new();
    public PcpRelayFanoutOptions Fanout { get; init; } = new();
    public int RelayPort { get; init; }
    public string ChannelName { get; init; } = "Local publish";
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(2);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Ingest);
        ArgumentNullException.ThrowIfNull(Http);
        ArgumentNullException.ThrowIfNull(Fanout);
        Ingest.Validate();
        Http.Validate();
        Fanout.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(RelayPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(RelayPort, 65535);
        ArgumentException.ThrowIfNullOrWhiteSpace(ChannelName);
        if (ChannelName.Length > 256 || DrainTimeout <= TimeSpan.Zero ||
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
