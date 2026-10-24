namespace PeerCast.Nova.Network.Rtmp;

/// <summary>Finite limits for one loopback OBS publishing generation.</summary>
public sealed record ObsRtmpIngestOptions
{
    public int Port { get; init; } = 7145;

    public string Application { get; init; } = "live";

    public string StreamName { get; init; } = "nova";

    public int MaximumMessageBytes { get; init; } = 1024 * 1024;

    public int MaximumBufferedBytes { get; init; } = 4 * 1024 * 1024;

    public int MaximumChunkStreams { get; init; } = 16;

    public int MaximumChunkBytes { get; init; } = 64 * 1024;

    public int MaximumBootstrapBytes { get; init; } = 64 * 1024;

    public int MaximumPacketBytes { get; init; } = 64 * 1024;

    /// <summary>Connected protocol I/O deadline, not a publisher arrival deadline.</summary>
    public TimeSpan IoTimeout { get; init; } = TimeSpan.FromSeconds(15);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(Port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Port, 65535);
        ArgumentException.ThrowIfNullOrWhiteSpace(Application);
        ArgumentException.ThrowIfNullOrWhiteSpace(StreamName);
        if (Application.Length > 128 || StreamName.Length > 128 ||
            Application.Contains('/', StringComparison.Ordinal) ||
            StreamName.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("Application and stream must be short single path components.");
        }

        if (MaximumMessageBytes is < 64 or > 4 * 1024 * 1024 ||
            MaximumBufferedBytes < MaximumMessageBytes ||
            MaximumBufferedBytes > 16 * 1024 * 1024 ||
            MaximumChunkStreams is < 1 or > 64 ||
            MaximumChunkBytes is < 128 or > 64 * 1024 ||
            MaximumPacketBytes is < 64 or > 64 * 1024 ||
            MaximumBootstrapBytes is < 64 or > 64 * 1024 ||
            MaximumBootstrapBytes > MaximumPacketBytes ||
            IoTimeout <= TimeSpan.Zero || IoTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentException("The RTMP ingest limits are outside the supported bounds.");
        }
    }
}

public enum ObsRtmpIngestEndReason
{
    PublisherStopped,
    Disconnected,
    Cancelled,
    TimedOut,
    ProtocolRejected,
    ConsumerRejected,
    TransportFailed,
    Quiesced,
    InternalFailure,
}

/// <summary>Managed delivery accounting; does not establish playback acceptance.</summary>
public sealed record ObsRtmpIngestResult(
    ObsRtmpIngestEndReason EndReason,
    long AcceptedPacketCount,
    long AcceptedByteCount)
{
    public ObsRtmpRejectionDiagnostic? Rejection { get; init; }
}

public enum ObsRtmpQuiescenceOutcome { Completed, TimedOut, Cancelled, Failed, AlreadyEnded }

/// <summary>
/// A bounded wait result. FinalResult is present only after the ingress has
/// terminated and its accepted counters cannot change. A timed-out/cancelled
/// wait does not abort an admitted callback; Completion remains the final barrier.
/// </summary>
public sealed record ObsRtmpQuiescenceResult(
    ObsRtmpQuiescenceOutcome Outcome,
    ObsRtmpIngestResult? FinalResult);
