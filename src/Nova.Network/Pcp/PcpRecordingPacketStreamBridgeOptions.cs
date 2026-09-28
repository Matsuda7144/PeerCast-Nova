namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Finite buffering limits for PCP packet-to-recording stream adaptation.
/// </summary>
public sealed record PcpRecordingPacketStreamBridgeOptions
{
    public int PauseWriterThresholdBytes { get; init; } = 1024 * 1024;

    public int ResumeWriterThresholdBytes { get; init; } = 512 * 1024;

    public int MinimumSegmentSizeBytes { get; init; } = 16 * 1024;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            PauseWriterThresholdBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            ResumeWriterThresholdBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MinimumSegmentSizeBytes);

        if (ResumeWriterThresholdBytes >= PauseWriterThresholdBytes)
        {
            throw new InvalidOperationException(
                $"{nameof(ResumeWriterThresholdBytes)} must be less than {nameof(PauseWriterThresholdBytes)}.");
        }

        if (MinimumSegmentSizeBytes > PauseWriterThresholdBytes)
        {
            throw new InvalidOperationException(
                $"{nameof(MinimumSegmentSizeBytes)} cannot exceed {nameof(PauseWriterThresholdBytes)}.");
        }
    }
}
