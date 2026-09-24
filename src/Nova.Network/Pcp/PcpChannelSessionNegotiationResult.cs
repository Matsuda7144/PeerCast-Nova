namespace PeerCast.Nova.Network.Pcp;

public enum PcpChannelSessionNegotiationOutcome
{
    Accepted = 1,
    Rejected = 2,
}

/// <summary>
/// Privacy-safe result published when one channel session either enters its
/// first accepted receive phase or receives a terminal handshake rejection.
/// It contains no host, channel, peer, or exception detail. The compatibility
/// outcome is the protocol classification observed by the handshake.
/// </summary>
public sealed record PcpChannelSessionNegotiationResult
{
    internal PcpChannelSessionNegotiationResult(
        PcpChannelSessionNegotiationOutcome outcome,
        int httpStatusCode,
        PcpCompatibilityOutcome? compatibilityOutcome = null)
    {
        Outcome = outcome;
        HttpStatusCode = httpStatusCode;
        CompatibilityOutcome = compatibilityOutcome;
    }

    public PcpChannelSessionNegotiationOutcome Outcome { get; }

    public int HttpStatusCode { get; }

    public PcpCompatibilityOutcome? CompatibilityOutcome { get; }

}
